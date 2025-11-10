using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Thea.Logging;

namespace Thea.MessageDriven;

class MessageDrivenService : IMessageDriven
{
    private readonly Task task;
    private readonly TimeSpan heartbeatCycle;
    private readonly CancellationTokenSource cancellationSource = new();
    private readonly EventWaitHandle readyToStart = new EventWaitHandle(false, EventResetMode.AutoReset);
    private readonly ConcurrentDictionary<string, List<RabbitConsumer>> consumers = new();
    private readonly ConcurrentDictionary<string, ConsumerWaiter> waitStartingConsumers = new();
    private readonly ConcurrentDictionary<string, List<RabbitConsumer>> waitShutdownConsumers = new();
    private readonly ConcurrentDictionary<string, DateTime> heartbeats = new();
    private readonly ConcurrentDictionary<string, RpcWaiter> rpcWaiters = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource<bool>> transferWaiters = new();
    private readonly Channel<Message> channel = Channel.CreateBounded<Message>(new BoundedChannelOptions(500)
    {
        FullMode = BoundedChannelFullMode.Wait,
        SingleWriter = false,
        SingleReader = true
    });

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
    private string lastNodeIds = null;
    private int sacCount = 2;

    private readonly Dictionary<string, Dictionary<string, MethodInfo>> consumerHandlers = new();
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
                        var waiters = this.rpcWaiters.Values.Where(f => DateTime.Now.Subtract(f.CreatedAt).TotalSeconds > f.TimeoutSeconds).ToList();
                        waiters.ForEach(f => f.Waiter.TrySetException(new TimeoutException($"RPC请求超时, 耗时{DateTime.Now.Subtract(f.CreatedAt).TotalSeconds}s")));
                        waiters.Clear();
                    }
                    if (this.channel.Reader.TryRead(out var message))
                    {
                        string queueId = null;
                        string queueName = null;
                        switch (message.Type)
                        {
                            case Consts.UserMessage:
                            case Consts.RpcMessage:
                                var properties = new BasicProperties
                                {
                                    Persistent = true,
                                    Type = message.Type.ToString(),
                                    DeliveryMode = DeliveryModes.Persistent,
                                    AppId = this.AppId,
                                    MessageId = message.MessageId,
                                    Headers = new Dictionary<string, object> { { "TraceId", message.TraceId } }
                                };
                                if (message.ScheduleTimeUtc.HasValue)
                                {
                                    var delayMilliseconds = (long)message.ScheduleTimeUtc.Value.Subtract(DateTime.UtcNow).TotalMilliseconds;
                                    properties.Headers.Add("x-delay", delayMilliseconds);
                                }
                                else
                                {
                                    if (message.Type == Consts.RpcMessage)
                                    {
                                        //RPC消息，设置回复队列和过期时间
                                        properties.ReplyTo = message.From;
                                        properties.CorrelationId = message.MessageId;
                                    }
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
                                                await this.rabbitProducer.PublishAsync(message.Exchange, routingKey.ToString(), properties, message.Body.ToJson());
                                            }
                                            //转发时，要带上Exchange,RoutingKey
                                            else
                                            {
                                                properties.Headers.Add("Exchange", message.Exchange);
                                                properties.Headers.Add("RoutingKey", message.RoutingKey);
                                                await this.rabbitProducer.PublishAsync(Consts.DefaultExchange, $"{Consts.TransferExchange}.{message.Exchange}", properties, message.Body.ToJson());
                                            }
                                        }
                                        else await this.rabbitProducer.PublishAsync(message.Exchange, message.RoutingKey, properties, message.Body.ToJson());
                                    }
                                }
                                //防止条件问题阻塞后续消费
                                message.Waiter?.TrySetResult(true);
                                break;
                            case Consts.Heartbeat:
                                //用户消息堆积，心跳消息也会阻塞，此节点会被认为是异常节点，将会从可用节点中移除
                                var nodeId = (string)message.Body;
                                this.heartbeats.AddOrUpdate(nodeId, DateTime.Now, (k, o) => DateTime.Now);
                                message.Waiter?.TrySetResult(true);
                                break;
                            case Consts.WaitStarting:
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
                                        {
                                            var myRabbitConsumers = this.consumers.GetOrAdd(consumer.QueueName, new List<RabbitConsumer>());
                                            myRabbitConsumers.Add(consumer);
                                            await consumer.Start();
                                        }
                                        this.waitStartingConsumers.TryRemove(queueId, out _);
                                    }
                                }
                                message.Waiter?.TrySetResult(true);
                                break;
                            case Consts.WaitShutdowning:
                                queueName = (string)message.Body;
                                if (this.waitShutdownConsumers.TryRemove(queueName, out var rabbitConsumers))
                                {
                                    int closedCount = rabbitConsumers.Count;
                                    rabbitConsumers.ForEach(async f => await f.Shutdown(true));
                                    Console.WriteLine($"队列{queueName}已收到结束标志消息，关闭消费者{closedCount}个！！");
                                }
                                message.Waiter?.TrySetResult(true);
                                break;
                            case Consts.Logs:
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
        this.channel.Writer.Complete();
        this.cancellationSource.Cancel();
        this.rabbitProducer.Shutdown().Wait();
        foreach (var rabbitConsumers in this.consumers.Values)
            rabbitConsumers.ForEach(async f => await f.Shutdown());
        this.consumers.Clear();
        this.heartbeats.Clear();
        this.rabbitProducer.Shutdown().Wait();
        this.heartbeatConsumer?.Shutdown().Wait();
        this.rpcConsumer?.Shutdown();
        this.waitStartingConsumers.Clear();
        foreach (var rabbitConsumers in this.waitShutdownConsumers.Values)
            rabbitConsumers.ForEach(async f => await f.Shutdown());
        foreach (var rpcWaiter in this.rpcWaiters.Values)
            rpcWaiter.Waiter.TrySetException(new Exception("MessageDrivenService已经关闭"));
        this.rpcWaiters.Clear();
        if (this.task != null)
            this.task.Wait();
        this.cancellationSource.Dispose();
    }

    public async Task PublishAsync<TMessage>(string exchange, string routingKey, TMessage message, CancellationToken cancellationToken = default)
    {
        if (!this.bindings.Exists(f => f.ExchangeId == exchange))
        {
            var errMessage = $"未注册的交换机{exchange}，请使用UseProducer或是UseStatefulConsumer、UseSubscriber方法进行注册";
            this.logger.LogTagError("MessageDriven", errMessage);
            throw new Exception(errMessage);
        }
        if (message == null)
            throw new ArgumentNullException(nameof(message));
        var traceId = string.Empty;
        if (ScopeState.TryGetState(out var scopeState))
            traceId = scopeState.TraceId;
        await this.channel.Writer.WriteAsync(new Message
        {
            MessageId = ObjectId.NewId(),
            Type = Consts.UserMessage,
            TraceId = traceId,
            Exchange = exchange,
            RoutingKey = routingKey,
            Body = message
        }, cancellationToken);
    }
    public async Task PublishRpcAsync<TRequest>(string replyToQueue, string messageId, string exchange, string routingKey, TRequest request, CancellationToken cancellationToken = default)
    {
        if (!this.bindings.Exists(f => f.ExchangeId == exchange))
            throw new Exception($"未注册的交换机{exchange}，请使用UseProducer或是UseStatefulConsumer、UseSubscriber方法进行注册");
        if (request == null)
            throw new ArgumentNullException(nameof(request));

        if (string.IsNullOrEmpty(replyToQueue))
            throw new ArgumentNullException(nameof(replyToQueue));
        if (string.IsNullOrEmpty(messageId))
            throw new ArgumentNullException(nameof(messageId));

        var traceId = string.Empty;
        if (ScopeState.TryGetState(out var scopeState))
            traceId = scopeState.TraceId;
        await this.channel.Writer.WriteAsync(new Message
        {
            MessageId = messageId,
            From = replyToQueue,
            Type = Consts.RpcMessage,
            TraceId = traceId,
            Exchange = exchange,
            RoutingKey = routingKey,
            Body = request
        }, cancellationToken);
    }
    public async Task<TResponse> RequestAsync<TRequest, TResponse>(string exchange, string routingKey, TRequest request, int timeoutSeconds = 30, CancellationToken cancellationToken = default)
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
        if (request == null)
            throw new ArgumentNullException(nameof(request));

        var traceId = string.Empty;
        if (ScopeState.TryGetState(out var scopeState))
            traceId = scopeState.TraceId;
        var messageId = ObjectId.NewId();

        var rpcWaiter = new RpcWaiter { MessageId = messageId, TimeoutSeconds = timeoutSeconds };
        this.rpcWaiters.TryAdd(messageId, rpcWaiter);
        await this.channel.Writer.WriteAsync(new Message
        {
            MessageId = messageId,
            From = $"rpc.result.{this.ServiceId}",
            Type = Consts.RpcMessage,
            TraceId = traceId,
            Exchange = exchange,
            RoutingKey = routingKey,
            Body = request
        }, cancellationToken);

        //Console.WriteLine($"RpcMessage,Request Message, MessageId: {theaMessage.MessageId}, From:{this.ServiceId}, DateTime: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        var rpcMessage = await rpcWaiter.Waiter.WithTimeout(TimeSpan.FromSeconds(timeoutSeconds));
        if (rpcMessage.Type == Consts.RpcFailure)
            throw new Exception(rpcMessage.Body);
        return rpcMessage.Body.JsonTo<TResponse>();
    }
    public async Task ScheduleAsync<TMessage>(string exchange, string routingKey, TMessage message, DateTime enqueueTimeUtc, CancellationToken cancellationToken = default)
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

        var traceId = string.Empty;
        if (ScopeState.TryGetState(out var scopeState))
            traceId = scopeState.TraceId;
        await this.channel.Writer.WriteAsync(new Message
        {
            MessageId = ObjectId.NewId(),
            Type = Consts.UserMessage,
            TraceId = traceId,
            Exchange = exchange,
            RoutingKey = routingKey,
            ScheduleTimeUtc = enqueueTimeUtc,
            Body = message.ToJson()
        }, cancellationToken);
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
        this.UseRpcConsumer();
    }
    public void UseStatefulConsumer(string exchange, string queue, MethodInfo methodInfo, bool isNeedTransfer = false)
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
                IsNeedTransfer = isNeedTransfer,
                CreatedBy = "MessageDrivenService",
                CreatedAt = DateTime.UtcNow,
                UpdatedBy = "MessageDrivenService",
                UpdatedAt = DateTime.UtcNow
            });
        }
        else
        {
            myBinding.BindType = Consts.TopicBindingType;
            myBinding.IsNeedTransfer = isNeedTransfer;
        }
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
    internal void UseRepository(IMessageDrivenRepository repository) => this.repository = repository;
    internal async Task ProcessMessage(Message message) => await this.channel.Writer.WriteAsync(message);
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
        await this.repository.Register(registerQueues, registerBindings);

        //创建交换机和队列及绑定
        string queueName = null;
        this.rabbitProducer = await RabbitProducer.Create(this, this.serviceProvider);
        if (this.isAllowCreateQueue)
        {
            foreach (var myQueue in this.queues)
            {
                if (!myQueue.IsEnabled) continue;
                if (myQueue.IsStateful)
                {
                    for (int i = 0; i < myQueue.WorkloadTotal; i++)
                    {
                        queueName = $"{myQueue.QueueId}.{i}";
                        await this.rabbitProducer.CreateQueue(queueName, myQueue.IsQuorum, myQueue.IsSac, false);
                    }
                }
                else await this.rabbitProducer.CreateQueue(myQueue.QueueId, myQueue.IsQuorum, false, false);

                //创建转发队列
                var myBindings = this.bindings.FindAll(f => f.QueueId == myQueue.QueueId && f.IsNeedTransfer);
                foreach (var myBinding in myBindings)
                {
                    if (!myBinding.IsNeedTransfer) continue;
                    queueName = $"{Consts.TransferExchange}.{myBinding.ExchangeId}";
                    await this.rabbitProducer.CreateQueue(queueName, myQueue.IsQuorum, true, false);
                }
            }
        }
        if (this.isAllowCreateExchange)
        {
            if (this.rpcExchanges.Count > 0 || this.isRpcConsumer)
            {
                var exchange = Consts.RpcExchange;
                await this.rabbitProducer.CreateExchange(exchange, Consts.TopicBindingType);
            }
            foreach (var myBinding in this.bindings)
                await rabbitProducer.CreateExchange(myBinding.ExchangeId, myBinding.BindType, myBinding.IsDelay);
        }
        if (this.isAllowCreateBinding)
        {
            foreach (var myBinding in this.bindings)
            {
                var myQueue = this.queues.Find(f => f.QueueId == myBinding.QueueId);
                if (myQueue.IsStateful)
                {
                    for (int i = 0; i < myQueue.WorkloadTotal; i++)
                    {
                        queueName = $"{myQueue.QueueId}.{i}";
                        await this.rabbitProducer.BindQueue(myBinding.ExchangeId, queueName, i.ToString());
                    }
                }
                else await this.rabbitProducer.BindQueue(myBinding.ExchangeId, myBinding.QueueId, Consts.FanoutRoutingKey);
            }
        }
        //创建RPC消费者
        if (this.isRpcConsumer)
        {
            var rpcQueueName = $"{Consts.RpcExchange}.result.{this.ServiceId}";
            this.rpcConsumer = new RabbitConsumer(rpcQueueName, rpcQueueName, this, this.serviceProvider, QueueType.RpcResult);
            await this.rpcConsumer.Start(Consts.RpcExchange, this.ServiceId);
        }
        (this.queues, this.bindings) = await this.repository.GetConfigInfo(false);
        if (!this.hasConsumer) return;

        if (this.isAllowCreateExchange)
            await this.rabbitProducer.CreateExchange(Consts.HeartbeatExchange, Consts.TopicBindingType);
        queueName = $"heartbeat.queue.{this.ServiceId}";
        this.heartbeatConsumer = new RabbitConsumer(queueName, queueName, this, this.serviceProvider, QueueType.Heartbeat);
        await this.heartbeatConsumer.Start(Consts.HeartbeatExchange, Consts.FanoutRoutingKey);
        //发送心跳消息
        await this.SendHeartbeat();
    }
    private async Task Initialize()
    {
        this.lastQueues = this.queues;
        (this.queues, this.bindings) = await this.repository.GetConfigInfo();
    }
    private async Task SendHeartbeat()
    {
        await this.rabbitProducer.PublishAsync(Consts.HeartbeatExchange, this.ServiceId, new BasicProperties
        {
            Persistent = true,
            Type = Consts.Heartbeat,
            DeliveryMode = DeliveryModes.Persistent,
            AppId = this.AppId,
            MessageId = ObjectId.NewId()
        }, this.ServiceId);
    }
    private async Task StartConsumers()
    {
        var removedKeys = this.heartbeats
            .Where(f => DateTime.Now.Subtract(f.Value) > this.heartbeatCycle * 2)
            .Select(f => f.Key).ToList();
        if (removedKeys.Count > 0)
            removedKeys.ForEach(f => this.heartbeats.TryRemove(f, out _));
        var nodeIds = this.heartbeats.Keys.ToList();
        nodeIds.Sort((x, y) => x.CompareTo(y));
        var currentNodeIds = string.Join(",", nodeIds);
        if (currentNodeIds != this.lastNodeIds)
            Console.WriteLine($"可用节点：{currentNodeIds}");

        //先创建有状态队列SAC激活消费者
        var myQueues = this.queues.Where(f => this.localQueueIds.Contains(f.QueueId) && f.IsEnabled && f.IsStateful)
            .OrderBy(f => f.QueueId).ToList();
        //先创建新增的队列和绑定
        if (this.lastQueues != null && this.lastQueues.Count > 0)
        {
            foreach (var lastQueue in this.lastQueues)
            {
                var lastWorkloadTotal = lastQueue.WorkloadTotal;
                var myQueue = myQueues.Find(f => f.QueueId == lastQueue.QueueId);
                if (myQueue == null || lastWorkloadTotal >= myQueue.WorkloadTotal)
                    continue;

                var myBindings = this.bindings.FindAll(f => f.QueueId == lastQueue.QueueId);
                for (int i = lastWorkloadTotal; i < myQueue.WorkloadTotal; i++)
                {
                    var queueName = $"{myQueue.QueueId}.{i}";
                    await this.CreateQueueAndBinding(queueName, lastQueue.IsQuorum, lastQueue.IsSac, false, i.ToString(), myBindings);
                }
            }
        }

        int index = 0, nodeCount = nodeIds.Count;
        List<RabbitConsumer> rabbitConsumers = null;
        var consumerIds = new Dictionary<string, List<string>>();
        //先创建有状态队列消费者
        foreach (var myQueue in myQueues)
        {
            var queueId = myQueue.QueueId;
            var lastWorkloadTotal = 0;
            if (this.lastQueues != null && this.lastQueues.Count > 0)
            {
                var lastQueue = this.lastQueues.Find(f => f.QueueId == queueId);
                if (lastQueue != null) lastWorkloadTotal = lastQueue.WorkloadTotal;
            }
            for (int i = 0; i < myQueue.WorkloadTotal; i++)
            {
                var nodeId = nodeCount > 1 ? nodeIds[index % nodeCount] : this.ServiceId;
                index++;
                if (nodeId != this.ServiceId) continue;

                //先创建激活的SAC消费者
                var queueName = $"{queueId}.{i}";
                rabbitConsumers = this.consumers.GetOrAdd(queueName, new List<RabbitConsumer>());
                var consumerId = $"{queueName}.{this.ServiceId}.0";
                if (!consumerIds.TryGetValue(queueName, out var myConsumerIds))
                    consumerIds.TryAdd(queueName, myConsumerIds = new());
                myConsumerIds.Add(consumerId);
                var myRabbitConsumer = rabbitConsumers.Find(f => f.ConsumerId == consumerId);
                if (myRabbitConsumer != null)
                {
                    if (!myRabbitConsumer.IsActivated)
                    {
                        await myRabbitConsumer.Shutdown(true);
                        await myRabbitConsumer.Start();
                    }
                    myRabbitConsumer.IsLogEnabled = myQueue.IsLogEnabled;
                    continue;
                }
                //新扩容的队列，还在等待之前队列的消息消费完成，还未启动
                if (this.waitStartingConsumers.TryGetValue(queueId, out var existingWaiter)
                    && existingWaiter.Consumers.Exists(f => f.ConsumerId == consumerId))
                    continue;

                if (lastWorkloadTotal > 0 && i > lastWorkloadTotal)
                {
                    var waiter = this.waitStartingConsumers.GetOrAdd(queueId, new ConsumerWaiter { WaitTotal = lastWorkloadTotal });
                    if (waiter.Consumers.Exists(f => f.ConsumerId == consumerId))
                        continue;
                    var exchangeHandlers = this.consumerHandlers[queueId];
                    myRabbitConsumer = new RabbitConsumer(queueName, consumerId, this, this.serviceProvider, QueueType.Message, myQueue.PrefetchCount, exchangeHandlers) { IsLogEnabled = myQueue.IsLogEnabled };
                    waiter.Consumers.Add(myRabbitConsumer);
                    Console.WriteLine($"有状态队列消费者{queueName}等待启动，当前等待启动的消费者数量：{waiter.Consumers.Count}");
                }
                else
                {
                    var exchangeHandlers = this.consumerHandlers[queueId];
                    myRabbitConsumer = new RabbitConsumer(queueName, consumerId, this, this.serviceProvider, QueueType.Message, myQueue.PrefetchCount, exchangeHandlers) { IsLogEnabled = myQueue.IsLogEnabled };
                    rabbitConsumers.Add(myRabbitConsumer);
                    await myRabbitConsumer.Start();
                    Console.WriteLine($"有状态队列消费者{consumerId}已启动，当前消费者数量：{rabbitConsumers.Count}");
                }
            }
            if (lastWorkloadTotal > 0)
            {
                //增加增加队列，要确定前面的队列都已经消费完毕，才能开始消费新加入的队列消息
                //往前面的几个队列发送结束标志消息，当消费者收到这个消息时，可以确定新加入的队列前消息都已经消费完毕，
                //此后新队列中的消息才可以进行消费，这样可以避免消息顺序错乱问题
                if (lastWorkloadTotal < myQueue.WorkloadTotal)
                {
                    for (int j = 0; j < lastWorkloadTotal; j++)
                    {
                        var queueName = $"{queueId}.{j}";
                        //已经消费完毕的队列名
                        await this.rabbitProducer.PublishAsync(Consts.DefaultExchange, queueName, new BasicProperties
                        {
                            Persistent = true,
                            AppId = this.AppId,
                            Type = Consts.WaitStarting,
                            DeliveryMode = DeliveryModes.Persistent,
                            MessageId = ObjectId.NewId()
                        }, queueName);
                        Console.WriteLine($"扩容队列{lastWorkloadTotal} -> {myQueue.WorkloadTotal}, 向队列{queueName}发送结束标志消息");
                    }
                }
                else if (lastWorkloadTotal > myQueue.WorkloadTotal)
                {
                    for (int j = myQueue.WorkloadTotal; j < lastWorkloadTotal; j++)
                    {
                        var queueName = $"{queueId}.{j}";
                        if (!this.consumers.TryRemove(queueName, out rabbitConsumers))
                            continue;
                        var hasMessage = false;
                        rabbitConsumers.ForEach(async f =>
                        {
                            if (await f.MessageCount() > 0)
                                hasMessage = true;
                        });
                        if (hasMessage)
                        {
                            this.waitShutdownConsumers[queueName] = rabbitConsumers;
                            //已经消费完毕的队列名
                            await this.rabbitProducer.PublishAsync(Consts.DefaultExchange, queueName, new BasicProperties
                            {
                                Persistent = true,
                                AppId = this.AppId,
                                Type = Consts.WaitShutdowning,
                                DeliveryMode = DeliveryModes.Persistent,
                                MessageId = ObjectId.NewId()
                            }, queueName);
                            Console.WriteLine($"收缩队列{lastWorkloadTotal} -> {myQueue.WorkloadTotal}, 向队列{queueName}发送结束标志消息");
                        }
                        else
                        {
                            rabbitConsumers.ForEach(async f => await f.Shutdown(true));
                            rabbitConsumers.Clear();
                        }
                    }
                }
            }
        }

        //然后创建转发队列SAC激活消费者
        var myExchangeIds = this.bindings.Where(f => f.IsNeedTransfer && this.localQueueIds.Contains(f.QueueId))
            .Select(f => f.ExchangeId).Distinct().OrderBy(f => f).ToList();
        if (myExchangeIds.Count > 0)
        {
            foreach (var exchangeId in myExchangeIds)
            {
                var nodeId = nodeCount > 1 ? nodeIds[index % nodeCount] : this.ServiceId;
                index++;
                if (nodeId != this.ServiceId) continue;

                var queueName = $"{Consts.TransferExchange}.{exchangeId}";
                var consumerId = $"{queueName}.{this.ServiceId}.0";
                rabbitConsumers = this.consumers.GetOrAdd(queueName, new List<RabbitConsumer>());
                if (!consumerIds.TryGetValue(queueName, out var myConsumerIds))
                    consumerIds.TryAdd(queueName, myConsumerIds = new());
                myConsumerIds.Add(consumerId);
                var myRabbitConsumer = rabbitConsumers.Find(f => f.ConsumerId == consumerId);
                if (myRabbitConsumer != null)
                {
                    if (!myRabbitConsumer.IsActivated)
                    {
                        await myRabbitConsumer.Shutdown(true);
                        await myRabbitConsumer.Start();
                    }
                    continue;
                }

                myRabbitConsumer = new RabbitConsumer(queueName, consumerId, this, this.serviceProvider, QueueType.Transfer);
                //为确保SAC消费者负载均衡，强制关闭多余的消费者，使当前消费者变成激活状态
                rabbitConsumers.Add(myRabbitConsumer);
                await myRabbitConsumer.Start();
                Console.WriteLine($"转发队列消费者{consumerId}已启动，当前消费者数量：{rabbitConsumers.Count}");
            }
        }

        //再创建无状态队列消费者
        myQueues = this.queues.Where(f => this.localQueueIds.Contains(f.QueueId) && f.IsEnabled && !f.IsStateful)
            .OrderBy(f => f.QueueId).ToList();
        foreach (var myQueue in myQueues)
        {
            var queueName = myQueue.QueueId;
            for (int i = 0; i < myQueue.WorkloadTotal; i++)
            {
                var nodeId = nodeCount > 1 ? nodeIds[index % nodeCount] : this.ServiceId;
                index++;
                if (nodeId != this.ServiceId) continue;

                var consumerId = $"{queueName}.{this.ServiceId}.{i}";
                rabbitConsumers = this.consumers.GetOrAdd(queueName, new List<RabbitConsumer>());
                if (!consumerIds.TryGetValue(queueName, out var myConsumerIds))
                    consumerIds.TryAdd(queueName, myConsumerIds = new());
                myConsumerIds.Add(consumerId);
                var myRabbitConsumer = rabbitConsumers.Find(f => f.ConsumerId == consumerId);
                if (myRabbitConsumer != null)
                {
                    if (!myRabbitConsumer.IsActivated)
                    {
                        await myRabbitConsumer.Shutdown(true);
                        await myRabbitConsumer.Start();
                    }
                    myRabbitConsumer.IsLogEnabled = myQueue.IsLogEnabled;
                    continue;
                }

                var exchangeHandlers = this.consumerHandlers[queueName];
                myRabbitConsumer = new RabbitConsumer(queueName, consumerId, this, this.serviceProvider, QueueType.Message, myQueue.PrefetchCount, exchangeHandlers) { IsLogEnabled = myQueue.IsLogEnabled };
                rabbitConsumers.Add(myRabbitConsumer);
                await myRabbitConsumer.Start();
                Console.WriteLine($"无状态队列消费者{consumerId}已启动，当前消费者数量：{rabbitConsumers.Count}");
            }
        }

        //再创建有状态队列SAC等待消费者
        myQueues = this.queues.Where(f => this.localQueueIds.Contains(f.QueueId) && f.IsEnabled && f.IsStateful && f.IsSac)
            .OrderBy(f => f.QueueId).ToList();
        foreach (var myQueue in myQueues)
        {
            var queueId = myQueue.QueueId;
            var lastWorkloadTotal = 0;
            if (this.lastQueues != null && this.lastQueues.Count > 0)
            {
                var lastQueue = this.lastQueues.Find(f => f.QueueId == queueId);
                if (lastQueue != null) lastWorkloadTotal = lastQueue.WorkloadTotal;
            }
            for (int i = 0; i < myQueue.WorkloadTotal; i++)
            {
                var nodeId = nodeCount > 1 ? nodeIds[index % nodeCount] : this.ServiceId;
                index++;
                //前面队列消费者已经清理过了，不会有多余的，不需要创建直接跳过
                if (nodeId != this.ServiceId) continue;

                var queueName = $"{queueId}.{i}";
                rabbitConsumers = this.consumers.GetOrAdd(queueName, new List<RabbitConsumer>());
                int workloadIndex = 1;
                while (workloadIndex < this.sacCount)
                {
                    var consumerId = $"{queueName}.{this.ServiceId}.{workloadIndex}";
                    if (!consumerIds.TryGetValue(queueName, out var myConsumerIds))
                        consumerIds.TryAdd(queueName, myConsumerIds = new());
                    myConsumerIds.Add(consumerId);
                    workloadIndex++;
                    var myRabbitConsumer = rabbitConsumers.Find(f => f.ConsumerId == consumerId);
                    if (myRabbitConsumer != null)
                    {
                        if (!myRabbitConsumer.IsActivated)
                        {
                            await myRabbitConsumer.Shutdown(true);
                            await myRabbitConsumer.Start();
                        }
                        myRabbitConsumer.IsLogEnabled = myQueue.IsLogEnabled;
                        continue;
                    }

                    var exchangeHandlers = this.consumerHandlers[queueId];
                    myRabbitConsumer = new RabbitConsumer(queueName, consumerId, this, this.serviceProvider, QueueType.Message, myQueue.PrefetchCount, exchangeHandlers) { IsLogEnabled = myQueue.IsLogEnabled };
                    if (lastWorkloadTotal > 0 && i > lastWorkloadTotal)
                    {
                        if (!this.waitStartingConsumers.TryGetValue(queueId, out var waiter))
                            this.waitStartingConsumers.TryAdd(queueId, waiter = new() { WaitTotal = lastWorkloadTotal });
                        waiter.Consumers.Add(myRabbitConsumer);
                        Console.WriteLine($"有状态队列消费者{queueName}等待启动，当前等待启动的消费者数量：{waiter.Consumers.Count}");
                    }
                    else
                    {
                        rabbitConsumers.Add(myRabbitConsumer);
                        await myRabbitConsumer.Start();
                        Console.WriteLine($"有状态队列消费者{queueName}已启动，SAC等待中，当前消费者数量：{rabbitConsumers.Count}");
                    }
                }
            }
        }

        //最后创建转发队列SAC等待消费者，转发队列就2个消费者      
        if (myExchangeIds.Count > 0)
        {
            foreach (var exchangeId in myExchangeIds)
            {
                var nodeId = nodeCount > 1 ? nodeIds[index % nodeCount] : this.ServiceId;
                index++;
                if (nodeId != this.ServiceId) continue;

                var queueName = $"{Consts.TransferExchange}.{exchangeId}";
                var consumerId = $"{queueName}.{this.ServiceId}.1";
                if (!consumerIds.TryGetValue(queueName, out var myConsumerIds))
                    consumerIds.TryAdd(queueName, myConsumerIds = new());
                myConsumerIds.Add(consumerId);
                rabbitConsumers = this.consumers.GetOrAdd(queueName, new List<RabbitConsumer>());
                var myRabbitConsumer = rabbitConsumers.Find(f => f.ConsumerId == consumerId);
                if (myRabbitConsumer != null)
                {
                    if (!myRabbitConsumer.IsActivated)
                    {
                        await myRabbitConsumer.Shutdown(true);
                        await myRabbitConsumer.Start();
                    }
                    continue;
                }

                myRabbitConsumer = new RabbitConsumer(queueName, consumerId, this, this.serviceProvider, QueueType.Transfer);
                //为确保SAC消费者负载均衡，强制关闭多余的消费者，使当前消费者变成激活状态
                rabbitConsumers.Add(myRabbitConsumer);
                await myRabbitConsumer.Start();
                Console.WriteLine($"转发队列消费者{queueName}已启动，SAC等待中，当前消费者数量：{rabbitConsumers.Count}");
            }
        }
        var consumerKeys = this.consumers.Keys.ToList();
        foreach (var queueName in this.consumers.Keys)
        {
            if (!consumerIds.TryGetValue(queueName, out var myConsumerIds))
            {
                if (this.consumers.TryRemove(queueName, out rabbitConsumers))
                {
                    while (rabbitConsumers.Count > 0)
                    {
                        var myRrabbitConsumer = rabbitConsumers.First();
                        rabbitConsumers.Remove(myRrabbitConsumer);
                        await myRrabbitConsumer.Shutdown(true);
                        Console.WriteLine($"多余消费者{myRrabbitConsumer.ConsumerId}已关闭");
                    }
                    Console.WriteLine($"队列{queueName}所有消费者已关闭");
                }
                continue;
            }
            rabbitConsumers = this.consumers[queueName];
            var removedConsumers = rabbitConsumers.FindAll(f => !myConsumerIds.Contains(f.ConsumerId));
            while (removedConsumers.Count > 0)
            {
                var myRrabbitConsumer = removedConsumers.First();
                rabbitConsumers.Remove(myRrabbitConsumer);
                removedConsumers.Remove(myRrabbitConsumer);
                await myRrabbitConsumer.Shutdown(true);
                Console.WriteLine($"多余消费者{myRrabbitConsumer.ConsumerId}已关闭");
            }
        }
        this.lastNodeIds = currentNodeIds;
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
}