using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using Thea.Logging;

namespace Thea.MessageDriven;

class MessageDrivenService : IMessageDriven
{
    private readonly Task task;
    private readonly TimeSpan heartbeatCycle;
    private readonly CancellationTokenSource cancellationSource = new();
    private readonly EventWaitHandle readyToStart = new EventWaitHandle(false, EventResetMode.AutoReset);
    private readonly ConcurrentDictionary<string, List<RabbitConsumer>> consumers = new();
    private readonly ConcurrentDictionary<string, List<RabbitConsumer>> transferConsumers = new();
    private readonly ConcurrentDictionary<string, ConsumerWaiter> waitStartingConsumers = new();
    private readonly ConcurrentDictionary<string, List<RabbitConsumer>> waitShutdownConsumers = new();
    private readonly ConcurrentDictionary<string, DateTime> heartbeats = new();
    private readonly ConcurrentDictionary<string, RpcWaiter> rpcWaiters = new();
    private readonly ConcurrentQueue<Message> messageQueue = new();

    private bool hasConsumer = false;
    private bool isAllowCreateQueue = false;
    private bool isAllowCreateExchange = false;
    private bool isAllowCreateBinding = false;
    private bool isRpcConsumer = false;
    private List<string> localExchanges = new();
    private List<string> localQueueIds = new();
    private List<string> rpcExchanges = new();
    private List<Queue> queues = new();
    private List<Queue> lastQueues = null;
    private List<Binding> bindings = new();
    private RabbitConsumer heartbeatConsumer;
    private RabbitConsumer rpcConsumer;
    private Func<string> traceIdFetcher;
    private int lastHashCode = 0;
    private int sacCount = 2;
    private bool isForceLoadBalance = false;
    private TimeSpan forceLoadBalanceInterval = TimeSpan.FromMinutes(10);

    private readonly Dictionary<string, Dictionary<string, MethodInfo>> consumerHandlers = new();
    private readonly Dictionary<string, Func<string, object, string>> exchangeSelectors = new();
    private readonly IServiceProvider serviceProvider;
    private readonly ILogger<MessageDrivenService> logger;
    private readonly bool isSac;
    private readonly bool isQuorum;
    private readonly TimeSpan rpcTimeout;
    private IMessageDrivenRepository repository;
    private DateTime lastInitedTime = DateTime.MinValue;
    private DateTime lastLoggedTime = DateTime.MinValue;
    private DateTime lastLoadBalanceTime = DateTime.MinValue;

    internal RabbitProducer rabbitProducer;
    internal List<AmqpTcpEndpoint> tcpEndPoints;
    public string AppId { get; private set; }
    public string ServiceId { get; private set; }

    public MessageDrivenService(IServiceProvider serviceProvider)
    {
        this.serviceProvider = serviceProvider;
        var hostAppLifetime = serviceProvider.GetService<IHostApplicationLifetime>();
        hostAppLifetime.ApplicationStopping.Register(() => this.Shutdown());

        this.logger = serviceProvider.GetService<ILogger<MessageDrivenService>>();
        var configuration = serviceProvider.GetService<IConfiguration>();

        this.AppId = configuration.GetValue<string>("AppId");
        var endPoints = configuration.GetSection("MessageDriven:EndPoints").Get<string[]>();
        if (endPoints == null || endPoints.Length == 0)
            throw new Exception("未设置MessageDriven:EndPoints，无法初始化MessageDrivenService对象");

        this.tcpEndPoints = endPoints.Select(f => AmqpTcpEndpoint.Parse(f)).ToList();
        this.isAllowCreateQueue = configuration.GetValue("MessageDriven:IsAllowCreateQueue", true);
        this.isAllowCreateExchange = configuration.GetValue("MessageDriven:IsAllowCreateExchange", true);
        this.isAllowCreateBinding = configuration.GetValue("MessageDriven:IsAllowCreateBinding", true);
        this.isSac = configuration.GetValue("MessageDriven:IsSac", true);
        this.isQuorum = configuration.GetValue("MessageDriven:IsQuorum", true);
        this.heartbeatCycle = TimeSpan.FromSeconds(configuration.GetValue("MessageDriven:Heartbeat", 10));
        this.rpcTimeout = TimeSpan.FromSeconds(configuration.GetValue("MessageDriven:RpcTimeout", 30));

        if (string.IsNullOrEmpty(this.AppId))
        {
            this.logger.LogTagError("MessageDriven", "未设置AppId，无法初始化MessageDrivenService对象");
            throw new Exception("未设置AppId，无法初始化MessageDrivenService对象");
        }
        this.sacCount = configuration.GetValue("MessageDriven:SacCount", 2);
        this.ServiceId = ObjectId.NewId();

        this.task = Task.Factory.StartNew(async () =>
        {
            this.readyToStart.WaitOne();
            var logs = new List<ExecLog>();
            while (!this.cancellationSource.IsCancellationRequested)
            {
                try
                {
                    //每10秒发送一次心跳，根据配置更新本地集群配置信息localQueues，配置中心或是数据库会有更改，比如：临时禁用某个集群
                    if (DateTime.Now - this.lastInitedTime >= this.heartbeatCycle)
                    {
                        if (this.hasConsumer) await this.SendHeartbeat();
                        await this.Initialize();
                        if (this.hasConsumer) await this.StartConsumers();
                        this.lastInitedTime = DateTime.Now;
                    }
                    if ((DateTime.Now - this.lastLoggedTime > TimeSpan.FromSeconds(10) && logs.Count > 0)
                        || logs.Count >= 100)
                    {
                        await this.repository.WriteLogs(logs);
                        logs.Clear();
                        this.lastLoggedTime = DateTime.Now;
                    }
                    if (this.rpcWaiters.Count > 0)
                    {
                        var waiters = this.rpcWaiters.Values.Where(f => DateTime.Now.Subtract(f.CreatedAt) > this.rpcTimeout).ToList();
                        waiters.ForEach(f => f.Waiter.TrySetException(new TimeoutException($"RPC请求超时, 耗时{DateTime.Now.Subtract(f.CreatedAt).TotalSeconds}s")));
                    }
                    if (this.messageQueue.TryDequeue(out var message))
                    {
                        string queueId = null;
                        string queueName = null;
                        switch (message.Type)
                        {
                            case MessageType.Message:
                            case MessageType.RpcMessage:
                                object theaMessage = new
                                {
                                    message.MessageId,
                                    message.From,
                                    message.Type,
                                    message.TraceId,
                                    message.Body,
                                    message.ScheduleTimeUtc
                                };
                                if (message.ScheduleTimeUtc.HasValue)
                                    this.rabbitProducer.Schedule(message.Exchange, message.RoutingKey, message.ScheduleTimeUtc.Value, theaMessage.ToJson());
                                else
                                {
                                    var myBindings = this.bindings.FindAll(f => f.ExchangeId == message.Exchange);
                                    foreach (var myBinding in myBindings)
                                    {
                                        var myQueue = this.queues.Find(f => f.QueueId == myBinding.QueueId);
                                        if (myQueue.IsStateful)
                                        {
                                            if (this.localQueueIds.Contains(myBinding.QueueId))
                                            {
                                                uint routingKey = 0;
                                                if (myQueue.WorkloadTotal > 1)
                                                {
                                                    var hashKey = Farmhash.Hash32(message.RoutingKey);
                                                    routingKey = (uint)(hashKey % myQueue.WorkloadTotal);
                                                }
                                                await this.rabbitProducer.Publish(message.Exchange, routingKey.ToString(), theaMessage.ToJson());
                                            }
                                            //转发时，要带上Exchange,RoutingKey
                                            else await this.rabbitProducer.Publish(Consts.DefaultExchange, $"{Consts.TransferExchange}.{message.Exchange}", message.ToJson());
                                        }
                                        else await this.rabbitProducer.Publish(message.Exchange, message.RoutingKey, theaMessage.ToJson());
                                    }
                                }
                                //防止条件问题阻塞后续消费
                                message.Waiter?.TrySetResult(true);
                                break;
                            case MessageType.Heartbeat:
                                //用户消息堆积，心跳消息也会阻塞，此节点会被认为是异常节点，将会从可用节点中移除
                                var nodeId = (string)message.Body;
                                this.heartbeats.AddOrUpdate(nodeId, DateTime.Now, (k, o) => DateTime.Now);
                                message.Waiter?.TrySetResult(true);
                                break;
                            case MessageType.WaitStarting:
                                queueName = (string)message.Body;
                                queueId = queueName.Substring(0, queueName.LastIndexOf('.'));
                                if (this.waitStartingConsumers.TryGetValue(queueId, out var consumerWaiter))
                                {
                                    if (!consumerWaiter.QueueNames.Contains(queueName))
                                        consumerWaiter.QueueNames.Add(queueName);
                                    Console.WriteLine($"队列{queueName}已收到结束标志消息，共接收到：{consumerWaiter.QueueNames.Count}个标志");

                                    if (consumerWaiter.QueueNames.Count >= consumerWaiter.WaitTotal)
                                    {
                                        Console.WriteLine($"所有队列均收到结束标志消息，共接收到：{consumerWaiter.QueueNames.Count}个标志，并启动所有消费者！！！");
                                        foreach (var consumer in consumerWaiter.Consumers)
                                            await consumer.Start();
                                        this.waitStartingConsumers.TryRemove(queueId, out _);
                                    }
                                }
                                message.Waiter?.TrySetResult(true);
                                break;
                            case MessageType.WaitShutdowning:
                                queueName = (string)message.Body;
                                if (this.waitShutdownConsumers.TryRemove(queueName, out var rabbitConsumers))
                                {
                                    int closedCount = rabbitConsumers.Count;
                                    rabbitConsumers.ForEach(async f => await f.Shutdown(true));
                                    Console.WriteLine($"队列{queueName}已收到结束标志消息，关闭消费者{closedCount}个！！");
                                }
                                message.Waiter?.TrySetResult(true);
                                break;
                            case MessageType.Logs:
                                logs.Add(message.Body as ExecLog);
                                break;
                        }
                    }
                    else Thread.Sleep(1);
                }
                catch (Exception ex)
                {
                    var exception = ex.InnerException ?? ex;
                    this.logger.LogTagError("MessageDriven", exception, "MessageDriven:消费者守护宿主线程执行异常");
                    logs.Clear();
                }
            }
        }, this.cancellationSource.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }
    public void Start()
    {
        this.Register().Wait();
        this.heartbeats.TryAdd(this.ServiceId, DateTime.Now);
        this.readyToStart.Set();
        Console.WriteLine($"Local ServiceId: {this.ServiceId}");
    }
    public void Shutdown()
    {
        this.cancellationSource.Cancel();
        this.rabbitProducer.Shutdown().Wait();
        foreach (var rabbitConsumers in this.consumers.Values)
            rabbitConsumers.ForEach(async f => await f.Shutdown());
        this.consumers.Clear();
        this.heartbeats.Clear();
        this.rabbitProducer.Shutdown().Wait();
        this.heartbeatConsumer?.Shutdown().Wait();
        this.rpcConsumer?.Shutdown();
        foreach (var rabbitConsumers in this.transferConsumers.Values)
            rabbitConsumers.ForEach(async f => await f.Shutdown());
        this.waitStartingConsumers.Clear();
        foreach (var rabbitConsumers in this.waitShutdownConsumers.Values)
            rabbitConsumers.ForEach(async f => await f.Shutdown());
        foreach (var rpcWaiter in this.rpcWaiters.Values)
            rpcWaiter.Waiter.TrySetException(new Exception("MessageDrivenService已经关闭"));

        if (this.task != null)
            this.task.Wait();
        this.cancellationSource.Dispose();
    }
    public async Task PublishAsync(string queue, object orgMessage)
    {
        if (orgMessage == null)
            throw new ArgumentNullException(nameof(orgMessage));
        await this.rabbitProducer.Publish(Consts.DefaultExchange, queue, orgMessage.ToJson());
    }
    public void Publish<TMessage>(string exchange, string routingKey, TMessage message)
    {
        if (!this.bindings.Exists(f => f.ExchangeId == exchange))
        {
            var errMessage = $"未注册的交换机{exchange}，请使用UseProducer或是UseStatefulConsumer、UseSubscriber方法进行注册";
            this.logger.LogTagError("MessageDriven", errMessage);
            throw new Exception(errMessage);
        }
        if (message == null)
            throw new ArgumentNullException(nameof(message));
        if (this.exchangeSelectors.TryGetValue(exchange, out var exchangeSelector))
            exchange = exchangeSelector.Invoke(exchange, message);
        this.messageQueue.Enqueue(new Message
        {
            MessageId = ObjectId.NewId(),
            Type = MessageType.Message,
            TraceId = this.traceIdFetcher?.Invoke(),
            Exchange = exchange,
            RoutingKey = routingKey,
            Body = message.ToJson()
        });
    }
    public Task PublishAsync<TMessage>(string exchange, string routingKey, TMessage message)
    {
        this.Publish(exchange, routingKey, message);
        return Task.CompletedTask;
    }
    public void PublishRpc<TMessage>(string serviceId, string messageId, string exchange, string routingKey, TMessage message)
    {
        if (!this.bindings.Exists(f => f.ExchangeId == exchange))
        {
            var errMessage = $"未注册的交换机{exchange}，请使用UseProducer或是UseStatefulConsumer、UseSubscriber方法进行注册";
            this.logger.LogTagError("MessageDriven", errMessage);
            throw new Exception(errMessage);
        }
        if (message == null)
            throw new ArgumentNullException(nameof(message));

        if (this.exchangeSelectors.TryGetValue(exchange, out var exchangeSelector))
            exchange = exchangeSelector.Invoke(exchange, message);

        if (string.IsNullOrEmpty(serviceId))
            serviceId = this.ServiceId;
        if (string.IsNullOrEmpty(messageId))
            messageId = ObjectId.NewId();
        var theaMessage = new Message
        {
            MessageId = messageId,
            From = serviceId,
            Type = MessageType.RpcMessage,
            TraceId = this.traceIdFetcher?.Invoke(),
            Exchange = exchange,
            RoutingKey = routingKey,
            Body = message.ToJson()
        };
        this.messageQueue.Enqueue(theaMessage);
    }
    public Task PublishRpcAsync<TMessage, TResponse>(string serviceId, string messageId, string exchange, string routingKey, TMessage message)
    {
        this.PublishRpc(serviceId, messageId, exchange, routingKey, message);
        return Task.CompletedTask;
    }
    public TResponse Request<TMessage, TResponse>(string exchange, string routingKey, TMessage message)
    {
        if (!this.bindings.Exists(f => f.ExchangeId == exchange))
        {
            var errMessage = $"未注册的交换机{exchange}，请使用UseProducer或是UseStatefulConsumer、UseSubscriber方法进行注册";
            this.logger.LogTagError("MessageDriven", errMessage);
            throw new Exception(errMessage);
        }
        if (!this.isRpcConsumer)
        {
            var errMessage = $"未配置RPC消费者，请使用方法：UseRpcConsumer()配置RPC消费者";
            this.logger.LogTagError("MessageDriven", errMessage);
            throw new Exception(errMessage);
        }
        if (message == null)
            throw new ArgumentNullException(nameof(message));

        if (this.exchangeSelectors.TryGetValue(exchange, out var exchangeSelector))
            exchange = exchangeSelector.Invoke(exchange, message);
        var theaMessage = new Message
        {
            MessageId = ObjectId.NewId(),
            From = this.ServiceId,
            Type = MessageType.RpcMessage,
            TraceId = this.traceIdFetcher?.Invoke(),
            Exchange = exchange,
            RoutingKey = routingKey,
            Body = message.ToJson()
        };
        var rpcWaiter = new RpcWaiter { MessageId = theaMessage.MessageId };
        this.rpcWaiters.TryAdd(theaMessage.MessageId, rpcWaiter);
        this.messageQueue.Enqueue(theaMessage);
        var rpcMessage = rpcWaiter.Waiter.Task.Result;
        if (rpcMessage.Type == MessageType.RpcFailure)
            throw new Exception(rpcMessage.Body);
        return rpcMessage.Body.JsonTo<TResponse>();
    }
    public async Task<TResponse> RequestAsync<TMessage, TResponse>(string exchange, string routingKey, TMessage message)
    {
        if (!this.bindings.Exists(f => f.ExchangeId == exchange))
        {
            var errMessage = $"未注册的交换机{exchange}，请使用UseProducer或是UseStatefulConsumer、UseSubscriber方法进行注册";
            this.logger.LogTagError("MessageDriven", errMessage);
            throw new Exception(errMessage);
        }
        if (!this.isRpcConsumer)
        {
            var errMessage = $"未配置RPC消费者，请使用方法：UseRpcConsumer()配置RPC消费者";
            this.logger.LogTagError("MessageDriven", errMessage);
            throw new Exception(errMessage);
        }
        if (message == null)
            throw new ArgumentNullException(nameof(message));

        if (this.exchangeSelectors.TryGetValue(exchange, out var exchangeSelector))
            exchange = exchangeSelector.Invoke(exchange, message);
        var theaMessage = new Message
        {
            MessageId = ObjectId.NewId(),
            From = this.ServiceId,
            Type = MessageType.RpcMessage,
            TraceId = this.traceIdFetcher?.Invoke(),
            Exchange = exchange,
            RoutingKey = routingKey,
            Body = message.ToJson()
        };
        var rpcWaiter = new RpcWaiter { MessageId = theaMessage.MessageId };
        this.rpcWaiters.TryAdd(theaMessage.MessageId, rpcWaiter);
        this.messageQueue.Enqueue(theaMessage);
        var rpcMessage = await rpcWaiter.Waiter.Task;
        if (rpcMessage.Type == MessageType.RpcFailure)
            throw new Exception(rpcMessage.Body);
        return rpcMessage.Body.JsonTo<TResponse>();
    }
    public void Schedule<TMessage>(string exchange, string routingKey, TMessage message, DateTime enqueueTimeUtc)
    {
        if (enqueueTimeUtc < DateTime.UtcNow)
            throw new Exception($"入队时间晚于现在时间，只能选择未来时间");
        if (!this.bindings.Exists(f => f.ExchangeId == exchange))
        {
            var errMessage = $"未注册的交换机{exchange}，请使用UseProducer或是UseStatefulConsumer、UseSubscriber方法进行注册";
            this.logger.LogTagError("MessageDriven", errMessage);
            throw new Exception(errMessage);
        }
        if (message == null)
            throw new ArgumentNullException(nameof(message));

        if (this.exchangeSelectors.TryGetValue(exchange, out var exchangeSelector))
            exchange = exchangeSelector.Invoke(exchange, message);
        this.messageQueue.Enqueue(new Message
        {
            MessageId = ObjectId.NewId(),
            Type = MessageType.Message,
            TraceId = this.traceIdFetcher?.Invoke(),
            Exchange = exchange,
            RoutingKey = routingKey,
            ScheduleTimeUtc = enqueueTimeUtc,
            Body = message.ToJson()
        });
    }
    public Task ScheduleAsync<TMessage>(string exchange, string routingKey, TMessage message, DateTime enqueueTimeUtc)
    {
        this.Schedule(exchange, routingKey, message, enqueueTimeUtc);
        return Task.CompletedTask;
    }
    public void UseProducer(params string[] exchanges)
    {
        foreach (var exchange in exchanges)
        {
            if (this.localExchanges.Exists(f => f == exchange))
                continue;
            this.localExchanges.Add(exchange);
        }
    }
    public void UseProducer(string exchange, bool isUseRpc)
    {
        if (!this.localExchanges.Contains(exchange))
            this.localExchanges.Add(exchange);
        if (isUseRpc && !this.rpcExchanges.Contains(exchange))
            this.rpcExchanges.Add(exchange);
    }
    public void UseStatefulConsumer(string exchange, string queue, MethodInfo methodInfo)
    {
        if (methodInfo == null) throw new ArgumentNullException(nameof(methodInfo));

        this.hasConsumer = true;
        if (!this.consumerHandlers.TryGetValue(queue, out var exchangeHandlers))
            this.consumerHandlers.TryAdd(queue, exchangeHandlers = new());
        exchangeHandlers.TryAdd(exchange, methodInfo);
        var myQueue = this.queues.Find(f => f.QueueId == queue);
        if (myQueue == null)
        {
            this.queues.Add(new Queue
            {
                QueueId = queue,
                QueueName = queue,
                IsQuorum = this.isQuorum,
                IsStateful = true,
                IsSac = true,
                PrefetchCount = 250,
                WorkloadTotal = 2,
                IsEnabled = true,
                IsLogEnabled = false,
                CreatedBy = "MessageDrivenService",
                CreatedAt = DateTime.UtcNow,
                UpdatedBy = "MessageDrivenService",
                UpdatedAt = DateTime.UtcNow
            });
        }
        else
        {
            myQueue.IsQuorum = this.isQuorum;
            myQueue.IsStateful = true;
            myQueue.IsSac = true;
            myQueue.PrefetchCount = 250;
            myQueue.WorkloadTotal = 2;
        }
        if (!this.localQueueIds.Contains(queue))
            this.localQueueIds.Add(queue);

        var myBinding = this.bindings.Find(f => f.ExchangeId == exchange && f.QueueId == queue);
        if (myBinding == null)
        {
            this.bindings.Add(new Binding
            {
                ExchangeId = exchange,
                BindType = Consts.TopicBindingType,
                QueueId = queue,
                CreatedBy = "MessageDrivenService",
                CreatedAt = DateTime.UtcNow,
                UpdatedBy = "MessageDrivenService",
                UpdatedAt = DateTime.UtcNow
            });
        }
        else myBinding.BindType = Consts.TopicBindingType;
    }
    public void UseSubscriber(string exchange, string queue, MethodInfo methodInfo, string routingKey = "#", bool isDelay = false)
    {
        if (methodInfo == null) throw new ArgumentNullException(nameof(methodInfo));

        this.hasConsumer = true;
        if (!this.consumerHandlers.TryGetValue(queue, out var exchangeHandlers))
            this.consumerHandlers.TryAdd(queue, exchangeHandlers = new());
        exchangeHandlers.TryAdd(exchange, methodInfo);
        //无状态队列，不同的队列不同的消费者，根据不同的routingKey路由到不同的队列中
        var myQueue = this.queues.Find(f => f.QueueId == queue);
        if (myQueue == null)
        {
            this.queues.Add(new Queue
            {
                QueueId = queue,
                QueueName = queue,
                IsQuorum = this.isQuorum,
                IsStateful = false,
                IsSac = false,
                PrefetchCount = 250,
                WorkloadTotal = 2,
                IsEnabled = true,
                IsLogEnabled = false,
                CreatedBy = "MessageDrivenService",
                CreatedAt = DateTime.UtcNow,
                UpdatedBy = "MessageDrivenService",
                UpdatedAt = DateTime.UtcNow
            });
        }
        else
        {
            myQueue.IsQuorum = this.isQuorum;
            myQueue.IsStateful = false;
            myQueue.IsSac = false;
            myQueue.PrefetchCount = 250;
            myQueue.WorkloadTotal = 2;
        }
        if (!this.localQueueIds.Contains(queue))
            this.localQueueIds.Add(queue);

        var bindingType = isDelay ? Consts.DelayBindingType : Consts.TopicBindingType;
        var myBinding = this.bindings.Find(f => f.ExchangeId == exchange && f.QueueId == queue);
        if (myBinding == null)
        {
            this.bindings.Add(new Binding
            {
                ExchangeId = exchange,
                QueueId = queue,
                BindType = bindingType,
                BindingKey = routingKey,
                IsDelay = isDelay,
                CreatedBy = "MessageDrivenService",
                CreatedAt = DateTime.UtcNow,
                UpdatedBy = "MessageDrivenService",
                UpdatedAt = DateTime.UtcNow
            });
        }
        else
        {
            myBinding.BindType = bindingType;
            myBinding.BindingKey = routingKey;
            myBinding.IsDelay = isDelay;
        }
    }
    public void UseRpcConsumer() => this.isRpcConsumer = true;
    public async Task ChangeQueue(string queueId, int workloadTotal)
    {
        var myQueue = this.queues.Find(f => f.QueueId == queueId);
        if (myQueue == null) return;
        var oldWorkloadTotal = myQueue.WorkloadTotal;
        if (myQueue == null || !myQueue.IsEnabled || oldWorkloadTotal == workloadTotal) return;
        if (myQueue.IsStateful && workloadTotal > oldWorkloadTotal)
        {
            var myBindings = this.bindings.FindAll(f => f.QueueId == queueId);
            for (int i = oldWorkloadTotal; i < workloadTotal; i++)
            {
                var queueName = $"{queueId}.{i}";
                if (this.isAllowCreateQueue)
                    await this.rabbitProducer.CreateQueue(queueName, myQueue.IsQuorum, myQueue.IsSac, false);
                if (this.isAllowCreateBinding)
                {
                    foreach (var myBinding in myBindings)
                    {
                        await this.rabbitProducer.BindQueue(myBinding.ExchangeId, queueName, i.ToString());
                    }
                }
            }
        }
        await this.repository.ChangeQueue(queueId, workloadTotal);
    }
    public async Task RemoveQueue(string queueId, int minIndex, int maxIndex)
    {
        var myBindings = this.bindings.FindAll(f => f.QueueId == queueId);
        for (int i = minIndex; i <= maxIndex; i++)
        {
            var queueName = $"{queueId}.{i}";
            await this.rabbitProducer.RemoveQueue(queueName);
        }
    }
    public void UseStrategy(string exchange, Func<string, object, string> exchangeSelector)
    {
        if (exchangeSelector == null)
            throw new ArgumentNullException(nameof(exchangeSelector));
        this.exchangeSelectors.Add(exchange, exchangeSelector);
    }
    internal void UseRepository(IMessageDrivenRepository repository) => this.repository = repository;
    internal void UseTraceIdFetcher(Func<string> traceIdFetcher) => this.traceIdFetcher = traceIdFetcher;
    internal void UseLoadBalance(bool isForceLoadBalancePerInterval, int intervalMinutes = 10)
    {
        this.isForceLoadBalance = isForceLoadBalancePerInterval;
        this.forceLoadBalanceInterval = TimeSpan.FromMinutes(intervalMinutes);
    }
    internal void AddLogs(ExecLog logInfo) => this.messageQueue.Enqueue(new Message
    {
        Type = MessageType.Logs,
        Body = logInfo
    });
    internal void ProcessMessage(Message message) => this.messageQueue.Enqueue(message);
    internal void SetRpcResult(string messageId, Message<string> result)
    {
        if (this.rpcWaiters.TryRemove(messageId, out var rpcWaiter))
            rpcWaiter.Waiter.TrySetResult(result);
    }
    private async Task Register()
    {
        //捞取数据库或是配置中心的集群信息        
        (var dbQueues, var dbBindings) = await this.repository.GetConfigInfo(false);
        var registerQueues = new List<Queue>();
        var registerBindings = new List<Binding>();
        foreach (var myQueue in this.queues)
        {
            if (dbQueues != null && dbQueues.Exists(f => f.QueueId == myQueue.QueueId))
                continue;
            registerQueues.Add(myQueue);
        }
        foreach (var myBinding in this.bindings)
        {
            if (dbBindings != null && dbBindings.Exists(f => f.ExchangeId == myBinding.ExchangeId && f.QueueId == myBinding.QueueId))
                continue;
            registerBindings.Add(myBinding);
        }
        //代码中有配置集群信息，但是数据库或是配置中心没有，需要注册，如果需要删除集群配置，需要在代码中要删除
        var isChanged = await this.repository.Register(registerQueues, registerBindings);

        string queueName = null;
        this.rabbitProducer = await RabbitProducer.Create(this, this.serviceProvider);
        if (this.rpcExchanges.Count > 0 || this.isRpcConsumer)
        {
            var exchange = Consts.RpcExchange;
            if (this.isAllowCreateExchange)
                await this.rabbitProducer.CreateExchange(exchange, Consts.TopicBindingType);
        }
        if (this.isRpcConsumer)
        {
            var rpcQueueName = $"{Consts.RpcExchange}.result.{this.ServiceId}";
            this.rpcConsumer = new RabbitConsumer(rpcQueueName, this, this.serviceProvider, QueueType.RpcResult);
            await this.rpcConsumer.Start(Consts.RpcExchange, this.ServiceId);
        }
        (this.queues, this.bindings) = await this.repository.GetConfigInfo(false);

        //只有生产者时并且与交换机绑定的队列是有状态队列，就需要创建转发队列，转发消息给有消费者的消息驱动组件再转发出去
        var changedBindings = new List<Binding>();
        var myBindings = this.bindings.FindAll(f => this.localExchanges.Contains(f.ExchangeId));
        foreach (var myBinding in myBindings)
        {
            var refQueue = this.queues.Find(f => f.QueueId == myBinding.QueueId);
            if (refQueue == null || !refQueue.IsStateful)
                continue;
            if (this.localQueueIds.Contains(myBinding.QueueId))
                continue;

            //创建转发队列
            queueName = $"{Consts.TransferExchange}.{myBinding.ExchangeId}";
            if (this.isAllowCreateQueue)
                await this.rabbitProducer.CreateQueue(queueName, this.isQuorum, this.isSac, false);

            //防止每次都更新数据库
            if (!myBinding.IsNeedTransfer)
            {
                myBinding.IsNeedTransfer = true;
                changedBindings.Add(myBinding);
            }
        }
        if (changedBindings.Count > 0)
            await this.repository.ChangeBindings(changedBindings);
        if (!this.hasConsumer) return;

        if (this.isAllowCreateExchange)
            await this.rabbitProducer.CreateExchange(Consts.HeartbeatExchange, Consts.TopicBindingType);
        queueName = $"heartbeat.queue.{this.ServiceId}";
        this.heartbeatConsumer = new RabbitConsumer(queueName, this, this.serviceProvider, QueueType.Heartbeat);
        await this.heartbeatConsumer.Start(Consts.HeartbeatExchange, Consts.FanoutRoutingKey);

        //创建交换机和队列
        if (isChanged)
            await this.Register(registerQueues, registerBindings);
        await this.Register(this.queues, this.bindings);
        await this.SendHeartbeat();
    }
    private async Task Register(List<Queue> registerQueues, List<Binding> registerBindings)
    {
        List<Binding> myBindings = null;
        foreach (var queue in registerQueues)
        {
            if (!queue.IsEnabled)
                continue;

            myBindings = registerBindings.FindAll(f => f.QueueId == queue.QueueId);
            if (this.isAllowCreateExchange)
            {
                foreach (var myBinding in myBindings)
                    await rabbitProducer.CreateExchange(myBinding.ExchangeId, myBinding.BindType, myBinding.IsDelay);
            }
            if (queue.IsStateful)
            {
                for (int i = 0; i < queue.WorkloadTotal; i++)
                    await this.CreateQueueAndBinding($"{queue.QueueId}.{i}", queue.IsQuorum, queue.IsSac, false, i.ToString(), myBindings);
            }
            else await this.CreateQueueAndBinding(queue.QueueId, queue.IsQuorum, false, false, Consts.FanoutRoutingKey, myBindings);
        }
        foreach (var myBinding in myBindings)
        {
            if (!myBinding.IsNeedTransfer)
                continue;
            var queueName = $"{Consts.TransferExchange}.{myBinding.ExchangeId}";
            if (this.isAllowCreateQueue)
                await this.rabbitProducer.CreateQueue(queueName, this.isQuorum, this.isSac, false);
        }
    }
    private async Task Initialize()
    {
        this.lastQueues = this.queues;
        (this.queues, this.bindings) = await this.repository.GetConfigInfo();
    }
    private async Task SendHeartbeat()
    {
        await this.rabbitProducer.Publish(Consts.HeartbeatExchange, this.ServiceId, new Message
        {
            MessageId = ObjectId.NewId(),
            Type = MessageType.Heartbeat,
            From = this.AppId,
            Body = this.ServiceId
        }.ToJson());
    }
    private async Task StartConsumers()
    {
        var removedKeys = this.heartbeats
            .Where(f => DateTime.Now.Subtract(f.Value) > this.heartbeatCycle * 2)
            .Select(f => f.Key).ToList();
        if (removedKeys.Count > 0)
            removedKeys.ForEach(f => this.heartbeats.TryRemove(f, out _));
        Console.WriteLine($"可用节点：{string.Join(",", this.heartbeats.Keys)}");
        var nodeIds = this.heartbeats.Keys.ToList();
        nodeIds.Sort((x, y) => x.CompareTo(y));

        var hashCode = this.GetHashCode(nodeIds);
        if (hashCode == this.lastHashCode && (!this.isForceLoadBalance || this.isForceLoadBalance
            && DateTime.Now.Subtract(this.lastLoadBalanceTime) < this.forceLoadBalanceInterval))
        {
            Console.WriteLine($"hashCode：{hashCode},lastHashCode：{this.lastHashCode}, same not need build, and return!!!");
            var allQueues = this.queues.FindAll(f => this.localQueueIds.Contains(f.QueueId) && f.IsEnabled);
            foreach (var myQueue in allQueues)
            {
                var queueId = myQueue.QueueId;
                if (myQueue.IsStateful)
                {
                    for (int i = 0; i < myQueue.WorkloadTotal; i++)
                    {
                        var queueName = $"{queueId}.{i}";
                        await this.CheckActive(queueName, myQueue.IsLogEnabled);
                    }
                }
                else await this.CheckActive(queueId, myQueue.IsLogEnabled);
            }
            return;
        }
        Console.WriteLine($"hashCode：{hashCode}, need build consumers, and starting!!!");

        int index = 0;
        var queueCounters = new Dictionary<string, int>();
        var queueWorkloads = new Dictionary<string, (bool, int)>();
        //先创建有状态队列SAC激活消费者
        var myQueues = this.queues.Where(f => this.localQueueIds.Contains(f.QueueId) && f.IsEnabled && f.IsStateful)
            .OrderBy(f => f.QueueId).ToList();
        foreach (var myQueue in myQueues)
        {
            var queueId = myQueue.QueueId;
            Queue oldQueue = null;
            var workloadTotal = myQueue.WorkloadTotal;
            if (this.lastQueues != null && this.lastQueues.Count > 0)
            {
                oldQueue = this.lastQueues.Find(f => f.QueueId == queueId);
                if (oldQueue != null && oldQueue.IsEnabled)
                    workloadTotal = oldQueue.WorkloadTotal;
            }
            //确保所有队列都已经创建并绑定
            var myBindings = this.bindings.FindAll(f => f.QueueId == queueId);
            for (int i = workloadTotal; i < myQueue.WorkloadTotal; i++)
            {
                var queueName = $"{queueId}.{i}";
                await this.CreateQueueAndBinding(queueName, myQueue.IsQuorum, myQueue.IsSac, false, i.ToString(), myBindings);
            }
            var isStarting = workloadTotal >= myQueue.WorkloadTotal;
            queueWorkloads.TryAdd(queueId, (isStarting, workloadTotal));
            for (int i = 0; i < myQueue.WorkloadTotal; i++)
            {
                var queueName = $"{queueId}.{i}";
                var exchangeHandlers = this.consumerHandlers[queueId];
                Func<string, RabbitConsumer> consumerBuilder = queueName => new RabbitConsumer(queueName, this, this.serviceProvider, QueueType.Message, myQueue.PrefetchCount, exchangeHandlers) { IsLogEnabled = myQueue.IsLogEnabled };
                if (!this.consumers.TryGetValue(queueName, out var rabbitConsumers))
                    this.consumers.TryAdd(queueName, rabbitConsumers = new());
                var needCount = await this.CreateConsumer(index, queueName, isStarting || i < workloadTotal, workloadTotal, nodeIds, queueCounters, rabbitConsumers, consumerBuilder);
                //为确保SAC消费者负载均衡，强制关闭多余的消费者，使当前消费者变成激活状态
                if (myQueue.IsSac) await this.RemoveNeedlessConsumers(needCount, rabbitConsumers);
                index++;
            }
            //增加增加队列，要确定前面的队列都已经消费完毕，才能开始消费新加入的队列消息
            //往前面的几个队列发送结束标志消息，当消费者收到这个消息时，可以确定新加入的队列前消息都已经消费完毕，
            //此后新队列中的消息才可以进行消费，这样可以避免消息顺序错乱问题
            if (workloadTotal < myQueue.WorkloadTotal)
            {
                var exchange = Consts.DefaultExchange;
                for (int j = 0; j < workloadTotal; j++)
                {
                    var queueName = $"{queueId}.{j}";
                    var message = new Message
                    {
                        MessageId = ObjectId.NewId(),
                        From = this.AppId,
                        Type = MessageType.WaitStarting,
                        //已经消费完毕的队列名
                        Body = queueName
                    };
                    //使用默认的交换机，路由键为队列名
                    await this.rabbitProducer.Publish(exchange, queueName, message.ToJson());
                    Console.WriteLine($"扩容队列{workloadTotal} -> {myQueue.WorkloadTotal}, 向队列{queueName}发送结束标志消息");
                }
            }
            else if (workloadTotal > myQueue.WorkloadTotal)
            {
                var exchange = Consts.DefaultExchange;
                for (int j = myQueue.WorkloadTotal; j < workloadTotal; j++)
                {
                    var queueName = $"{queueId}.{j}";
                    if (!this.consumers.TryRemove(queueName, out var rabbitConsumers))
                        continue;

                    var myRabbitConsumer = rabbitConsumers.First();
                    if (await myRabbitConsumer.MessageCount() > 0)
                    {
                        this.waitShutdownConsumers[queueName] = rabbitConsumers;
                        var message = new Message
                        {
                            MessageId = ObjectId.NewId(),
                            From = this.AppId,
                            Type = MessageType.WaitShutdowning,
                            //已经消费完毕的队列名
                            Body = queueName
                        };
                        //使用默认的交换机，路由键为队列名
                        await this.rabbitProducer.Publish(exchange, queueName, message.ToJson());
                        Console.WriteLine($"收缩队列{workloadTotal} -> {myQueue.WorkloadTotal}, 向队列{queueName}发送结束标志消息");
                    }
                    else
                    {
                        await this.RemoveNeedlessConsumers(0, rabbitConsumers);
                        Console.WriteLine($"收缩队列{workloadTotal} -> {myQueue.WorkloadTotal}, 队列{queueName}没有消息，直接删除消费者{rabbitConsumers.Count}个");
                    }
                }
            }
        }

        //然后创建转发队列SAC激活消费者
        var myExchangeIds = this.bindings.FindAll(f => f.IsNeedTransfer && this.localQueueIds.Contains(f.QueueId))
            .Select(f => f.ExchangeId).Distinct().OrderBy(f => f).ToList();
        if (myExchangeIds.Count > 0)
        {
            foreach (var exchangeId in myExchangeIds)
            {
                var queueName = $"{Consts.TransferExchange}.{exchangeId}";
                Func<string, RabbitConsumer> consumerBuilder = queueName => new RabbitConsumer(queueName, this, this.serviceProvider, QueueType.Transfer);
                if (!this.transferConsumers.TryGetValue(queueName, out var rabbitConsumers))
                    this.transferConsumers.TryAdd(queueName, rabbitConsumers = new());
                var needCount = await this.CreateConsumer(index, queueName, true, 0, nodeIds, queueCounters, rabbitConsumers, consumerBuilder);
                //为确保SAC消费者负载均衡，强制关闭多余的消费者，使当前消费者变成激活状态
                await this.RemoveNeedlessConsumers(needCount, rabbitConsumers);
                index++;
            }
        }

        //再创建有状态队列SAC等待消费者
        myQueues = this.queues.Where(f => this.localQueueIds.Contains(f.QueueId) && f.IsEnabled && f.IsStateful && f.IsSac)
            .OrderBy(f => f.QueueId).ToList();
        foreach (var myQueue in myQueues)
        {
            var queueId = myQueue.QueueId;
            var exchangeHandlers = this.consumerHandlers[queueId];
            Func<string, RabbitConsumer> consumerBuilder = queueName => new RabbitConsumer(queueName, this, this.serviceProvider, QueueType.Message, myQueue.PrefetchCount, exchangeHandlers) { IsLogEnabled = myQueue.IsLogEnabled };
            bool isStarting = false;
            int workloadTotal = myQueue.WorkloadTotal;
            if (queueWorkloads.TryGetValue(queueId, out var queueWorkload))
            {
                isStarting = queueWorkload.Item1;
                workloadTotal = queueWorkload.Item2;
            }
            for (int i = 0; i < myQueue.WorkloadTotal; i++)
            {
                var queueName = $"{queueId}.{i}";
                if (!this.consumers.TryGetValue(queueName, out var rabbitConsumers))
                    this.consumers.TryAdd(queueName, rabbitConsumers = new());
                int needCount = 0;
                for (int j = 1; j < this.sacCount; j++)
                {
                    needCount = await this.CreateConsumer(index, queueName, isStarting || i < workloadTotal, workloadTotal, nodeIds, queueCounters, rabbitConsumers, consumerBuilder);
                    index++;
                }
                if (myQueue.IsSac) await this.RemoveNeedlessConsumers(needCount, rabbitConsumers);
                if (rabbitConsumers.Count == 0)
                    this.consumers.TryRemove(queueName, out _);
            }
        }

        //再创建无状态队列消费者
        myQueues = this.queues.Where(f => this.localQueueIds.Contains(f.QueueId) && f.IsEnabled && !f.IsStateful)
            .OrderBy(f => f.QueueId).ToList();
        foreach (var myQueue in myQueues)
        {
            var queueId = myQueue.QueueId;
            if (!this.consumers.TryGetValue(queueId, out var rabbitConsumers))
                this.consumers.TryAdd(queueId, rabbitConsumers = new());
            var exchangeHandlers = this.consumerHandlers[queueId];
            Func<string, RabbitConsumer> consumerBuilder = queueName => new RabbitConsumer(queueName, this, this.serviceProvider, QueueType.Message, myQueue.PrefetchCount, exchangeHandlers) { IsLogEnabled = myQueue.IsLogEnabled };

            int needCount = 0;
            for (int i = 0; i < myQueue.WorkloadTotal; i++)
            {
                needCount = await this.CreateConsumer(index, queueId, true, 0, nodeIds, queueCounters, rabbitConsumers, consumerBuilder);
                index++;
            }
            await this.RemoveNeedlessConsumers(needCount, rabbitConsumers);
            if (rabbitConsumers.Count == 0)
                this.consumers.TryRemove(queueId, out _);
        }

        //最后创建转发队列SAC等待消费者，转发队列就2个消费者
        myExchangeIds = this.bindings.FindAll(f => f.IsNeedTransfer && this.localQueueIds.Contains(f.QueueId))
            .Select(f => f.ExchangeId).Distinct().OrderBy(f => f).ToList();
        if (myExchangeIds.Count > 0)
        {
            foreach (var exchangeId in myExchangeIds)
            {
                var queueName = $"{Consts.TransferExchange}.{exchangeId}";
                if (!this.transferConsumers.TryGetValue(queueName, out var rabbitConsumers))
                    this.transferConsumers.TryAdd(queueName, rabbitConsumers = new());
                Func<string, RabbitConsumer> consumerBuilder = queueName => new RabbitConsumer(queueName, this, this.serviceProvider, QueueType.Transfer);
                var needCount = await this.CreateConsumer(index, queueName, true, 0, nodeIds, queueCounters, rabbitConsumers, consumerBuilder);
                await this.RemoveNeedlessConsumers(needCount, rabbitConsumers);
                if (rabbitConsumers.Count == 0)
                    this.transferConsumers.TryRemove(queueName, out _);
                index++;
            }
        }
        this.lastHashCode = hashCode;
        this.lastLoadBalanceTime = DateTime.Now;
    }
    private async Task<int> CreateConsumer(int index, string queueName, bool isStarting, int waitReplyingCount, List<string> nodeIds, Dictionary<string, int> queueCounters, List<RabbitConsumer> rabbitConsumers, Func<string, RabbitConsumer> consumerBuilder)
    {
        var nodeCount = nodeIds.Count;
        var nodeId = nodeCount > 1 ? nodeIds[index % nodeCount] : this.ServiceId;
        if (!queueCounters.TryGetValue(queueName, out var needCount))
            needCount = 0;
        if (nodeId == this.ServiceId)
        {
            RabbitConsumer myRabbitConsumer = null;
            if (rabbitConsumers.Count > needCount)
                myRabbitConsumer = rabbitConsumers[needCount];
            else rabbitConsumers.Add(myRabbitConsumer = consumerBuilder.Invoke(queueName));
            if (isStarting)
            {
                if (!myRabbitConsumer.IsActivated)
                    await myRabbitConsumer.Shutdown(true);
                await myRabbitConsumer.Start();
            }
            else
            {
                var queueId = queueName.Substring(0, queueName.LastIndexOf('.'));
                if (!this.waitStartingConsumers.TryGetValue(queueId, out var waiter))
                    this.waitStartingConsumers.TryAdd(queueId, waiter = new() { WaitTotal = waitReplyingCount });
                waiter.Consumers.Add(myRabbitConsumer);
                Console.WriteLine($"消费者{queueName}等待启动，当前等待启动的消费者数量：{waiter.Consumers.Count}");
            }
            needCount++;
            queueCounters[queueName] = needCount;
        }
        return needCount;
    }
    private async Task RemoveNeedlessConsumers(int needCount, List<RabbitConsumer> rabbitConsumers)
    {
        while (rabbitConsumers.Count > needCount)
        {
            var myRabbitConsumer = rabbitConsumers.Last();
            //强制关闭消费者，消费者一定要做好幂等处理
            await myRabbitConsumer.Shutdown(true);
            rabbitConsumers.Remove(myRabbitConsumer);
        }
    }
    private async Task CheckActive(string queueName, bool isLogEnabled)
    {
        if (!this.consumers.TryGetValue(queueName, out var myRabbitConsumers))
            return;
        foreach (var rabbitConsumer in myRabbitConsumers)
        {
            rabbitConsumer.IsLogEnabled = isLogEnabled;
            if (rabbitConsumer.IsActivated)
                continue;
            await rabbitConsumer.Shutdown(true);
            await rabbitConsumer.Start();
        }
    }
    private async Task CreateQueueAndBinding(string queueName, bool isQuorum, bool isSac, bool isExclusive, string routingKey, List<Binding> myBindings)
    {
        if (this.isAllowCreateQueue)
            await this.rabbitProducer.CreateQueue(queueName, isQuorum, isSac, isExclusive);
        if (this.isAllowCreateBinding)
        {
            foreach (var myBinding in myBindings)
                await this.rabbitProducer.BindQueue(myBinding.ExchangeId, queueName, routingKey);
        }
    }
    private int GetHashCode(List<string> nodeIds)
    {
        var myStatefulQueues = this.queues.Where(f => this.localQueueIds.Contains(f.QueueId) && f.IsEnabled && f.IsStateful)
            .OrderBy(f => f.QueueId).Select(f => $"{f.QueueId}-{f.IsSac}-{f.WorkloadTotal}").ToList();
        var myExchanges = this.bindings.FindAll(f => f.IsNeedTransfer && this.localQueueIds.Contains(f.QueueId))
            .Select(f => f.ExchangeId).Distinct().OrderBy(f => f).ToList();
        var mySubscriberQueues = this.queues.Where(f => this.localQueueIds.Contains(f.QueueId) && f.IsEnabled && !f.IsStateful)
            .OrderBy(f => f.QueueId).Select(f => $"{f.QueueId}-{f.WorkloadTotal}").ToList();

        var hashCode = new HashCode();
        hashCode.Add(nodeIds.Count);
        nodeIds.ForEach(f => hashCode.Add(f));
        hashCode.Add(myStatefulQueues.Count);
        if (myStatefulQueues.Count == 0)
            hashCode.Add(0);
        else myStatefulQueues.ForEach(f => hashCode.Add(f));
        hashCode.Add(myExchanges.Count);
        if (myExchanges.Count == 0)
            hashCode.Add(0);
        else mySubscriberQueues.ForEach(f => hashCode.Add(f));
        hashCode.Add(mySubscriberQueues.Count);
        if (mySubscriberQueues.Count == 0)
            hashCode.Add(0);
        else mySubscriberQueues.ForEach(f => hashCode.Add(f));
        return hashCode.ToHashCode();
    }
}
