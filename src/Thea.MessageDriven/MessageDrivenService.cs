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
    private readonly ConcurrentDictionary<string, DateTime> shutdownQueues = new();
    private readonly ConcurrentDictionary<string, DateTime> heartbeats = new();
    private readonly ConcurrentDictionary<string, RpcWaiter> rpcWaiters = new();
    private readonly Channel<Message> channel = Channel.CreateBounded<Message>(new BoundedChannelOptions(5000)
    {
        FullMode = BoundedChannelFullMode.Wait,
        SingleWriter = false,
        SingleReader = true
    });
    private bool hasConsumer = false;
    private string lastNodeIds = null;
    private bool isRpcConsumer = false;
    private List<Binding> bindings = new();
    private List<Queue> lastQueues = null;
    private List<Queue> queues = new();
    private Dictionary<string, QueueState> localQueueStates;
    private List<ExchangeTransfer> exchangeTransfers = new();
    private ConfigInfo configInfo = new();
    private RabbitConsumer heartbeatConsumer;
    private RabbitConsumer rpcConsumer;

    private readonly Dictionary<string, Dictionary<string, MethodInfo>> consumerHandlers = new();
    private readonly IServiceProvider serviceProvider;
    private readonly ILogger<MessageDrivenService> logger;
    private IMessageDrivenRepository repository;
    private DateTime lastInitedTime = DateTime.MinValue;
    private DateTime lastLoggedTime = DateTime.MinValue;
    private DateTime lastClearRpcTime = DateTime.MinValue;

    internal RabbitProducer rabbitProducer;
    internal List<AmqpTcpEndpoint> tcpEndPoints;
    internal TimeSpan HeartbeatCycle => this.heartbeatCycle;
    public string AppId { get; private set; }
    public string ServiceId { get; private set; }

    public MessageDrivenService(IServiceProvider serviceProvider)
    {
        this.serviceProvider = serviceProvider;
        var hostAppLifetime = serviceProvider.GetService<IHostApplicationLifetime>();
        hostAppLifetime.ApplicationStopping.Register(this.Shutdown);

        this.logger = serviceProvider.GetService<ILogger<MessageDrivenService>>();
        var configuration = serviceProvider.GetService<IConfiguration>();
        configuration.GetSection("MessageDriven").Bind(this.configInfo);
        this.AppId = configuration.GetValue<string>("AppId");
        if (string.IsNullOrEmpty(this.AppId))
            throw new Exception("未设置AppId，无法初始化MessageDrivenService对象");
        if (this.configInfo.EndPoints == null || this.configInfo.EndPoints.Count == 0)
            throw new Exception("未设置MessageDriven:EndPoints，无法初始化MessageDrivenService对象");

        this.tcpEndPoints = this.configInfo.EndPoints.Select(f => AmqpTcpEndpoint.Parse(f)).ToList();
        this.heartbeatCycle = TimeSpan.FromSeconds(this.configInfo.Heartbeat);
        this.ServiceId = ObjectId.NewId();

        this.task = Task.Factory.StartNew(async () =>
        {
            this.readyToStart.WaitOne();
            var logs = new List<ExecLog>();
            while (!this.cancellationSource.IsCancellationRequested)
            {
                Message message = null;
                BasicProperties properties = null;
                try
                {
                    //每10秒发送一次心跳，根据配置更新本地集群配置信息localQueues，配置中心或是数据库会有更改，比如：临时禁用某个集群
                    if (DateTime.UtcNow - this.lastInitedTime >= this.heartbeatCycle)
                    {
                        await this.Initialize();
                        this.lastInitedTime = DateTime.UtcNow;
                    }
                    if ((DateTime.UtcNow - this.lastLoggedTime > TimeSpan.FromSeconds(10) && logs.Count > 0)
                        || logs.Count >= 100)
                    {
                        await this.repository.WriteLogs(logs);
                        logs.Clear();
                        this.lastLoggedTime = DateTime.UtcNow;
                    }
                    if (this.rpcWaiters.IsEmpty && DateTime.UtcNow.Subtract(this.lastClearRpcTime).TotalSeconds > 5)
                    {
                        var messageIds = this.rpcWaiters.Keys.ToList();
                        foreach (var messageId in messageIds)
                        {
                            if (!this.rpcWaiters.TryGetValue(messageId, out var rpcWaiter))
                                continue;
                            if (DateTime.UtcNow.Subtract(rpcWaiter.CreatedAt).TotalSeconds < rpcWaiter.TimeoutSeconds)
                                continue;
                            this.rpcWaiters.TryRemove(messageId, out _);
                            //只结束超时的RPC请求，不做清理工作，清理工作在Request方法中处理
                            rpcWaiter.Waiter.TrySetException(new TimeoutException(rpcWaiter.TimeoutMessage));
                        }
                        this.lastClearRpcTime = DateTime.UtcNow;
                    }
                    for (int i = 0; i < 10; i++)
                    {
                        if (!this.channel.Reader.TryRead(out message))
                            break;
                        switch (message.Type)
                        {
                            case Consts.UserMessage:
                            case Consts.RpcMessage:
                                properties = new BasicProperties
                                {
                                    Persistent = true,
                                    Type = message.Type,
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
                                if (message.Type == Consts.RpcMessage)
                                {
                                    //RPC消息，设置回复队列和过期时间
                                    properties.ReplyTo = message.ReplyTo;
                                    properties.CorrelationId = message.MessageId;
                                }
                                var queueIds = this.bindings.Where(f => f.ExchangeId == message.Exchange)
                                   .Select(f => f.QueueId).ToList();
                                var statefalQeues = this.queues.FindAll(f => queueIds.Contains(f.QueueId) && f.IsStateful);

                                var jsonMessage = message.IsJsonMessage ? message.Body.ToString() : message.Body.ToJson();
                                if (statefalQeues != null && statefalQeues.Count > 0)
                                {
                                    var myQueue = statefalQeues[0];
                                    if (myQueue.AppId == this.AppId)
                                    {
                                        //如果存在有状态队列，根据消息的RoutingKey进行一致性哈希，路由到对应的队列中
                                        var routingKey = JumpConsistentHash.GetBucket(message.RoutingKey, myQueue.WorkloadTotal);
                                        message.RoutingKey = routingKey.ToString();
                                        await this.rabbitProducer.PublishAsync(message.Exchange, routingKey.ToString(), properties, jsonMessage);
                                    }
                                    else
                                    {
                                        //如果队列消费者是其他应用的，发到转发队列中
                                        properties.Headers.Add("Exchange", message.Exchange);
                                        properties.Headers.Add("RoutingKey", message.RoutingKey);
                                        await this.rabbitProducer.PublishAsync(Consts.TransferExchange, myQueue.AppId, properties, jsonMessage);
                                    }
                                }
                                //如果是无状态队列的消息，直接发送交换机
                                else await this.rabbitProducer.PublishAsync(message.Exchange, message.RoutingKey, properties, jsonMessage);

                                //防止条件问题阻塞后续消费
                                message.Waiter?.TrySetResult(true);
                                break;
                            case Consts.Heartbeat:
                                //用户消息堆积，心跳消息也会阻塞，此节点会被认为是异常节点，将会从可用节点中移除
                                this.heartbeats.AddOrUpdate((string)message.Body, DateTime.UtcNow, (k, o) => DateTime.UtcNow);
                                break;
                            case Consts.Logs:
                                logs.Add(message.Body as ExecLog);
                                break;
                        }
                    }
                    if (!this.channel.Reader.TryPeek(out _))
                        Thread.Sleep(1);
                }
                catch (Exception ex)
                {
                    var exception = ex.InnerException ?? ex;
                    this.logger.LogTagError("MessageDriven", exception, $"Message: {message.ToJson()}, Properties: {properties.ToJson()}");
                    logs.Clear();
                }
            }
        }, this.cancellationSource.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }
    public void Start()
    {
        this.Register().Wait();
        this.heartbeats[this.ServiceId] = DateTime.UtcNow;
        this.readyToStart.Set();
        Console.WriteLine($"Local ServiceId: {this.ServiceId}");
    }
    public void Shutdown()
    {
        this.channel.Writer.TryComplete();
        this.cancellationSource.Cancel();
        this.rabbitProducer.Shutdown().Wait();
        foreach (var rabbitConsumers in this.consumers.Values)
            rabbitConsumers.ForEach(f => f.Shutdown().Wait());
        this.consumers.Clear();
        this.heartbeats.Clear();
        this.rabbitProducer.Shutdown().Wait();
        this.heartbeatConsumer?.Shutdown().Wait();
        this.rpcConsumer?.Shutdown();
        this.shutdownQueues.Clear();
        foreach (var rpcWaiter in this.rpcWaiters.Values)
            rpcWaiter.Waiter.TrySetException(new Exception("MessageDrivenService已经关闭"));
        this.rpcWaiters.Clear();
        if (this.task != null)
            this.task.Wait();
        this.cancellationSource.Dispose();
    }
    public async Task PublishAsync<TMessage>(string exchange, string routingKey, TMessage message, CancellationToken cancellationToken = default)
    {
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
            ReplyTo = replyToQueue,
            Type = Consts.RpcMessage,
            TraceId = traceId,
            Exchange = exchange,
            RoutingKey = routingKey,
            Body = request
        }, cancellationToken);
    }
    public async Task<TResponse> RequestAsync<TRequest, TResponse>(string exchange, string routingKey, TRequest request, int timeoutSeconds = 30, CancellationToken cancellationToken = default)
    {
        if (!this.isRpcConsumer)
            throw new Exception($"未配置RPC消费者，请使用方法：UseRpcConsumer()配置RPC消费者");
        if (request == null)
            throw new ArgumentNullException(nameof(request));

        var traceId = string.Empty;
        if (ScopeState.TryGetState(out var scopeState))
            traceId = scopeState.TraceId;
        var messageId = ObjectId.NewId();

        var exMessage = $"RPC请求超时, 耗时{timeoutSeconds}s, message: {request.ToJson()}, exchange: {exchange}, routingKey: {routingKey}";
        var rpcWaiter = new RpcWaiter { MessageId = messageId, TimeoutSeconds = timeoutSeconds, TimeoutMessage = exMessage };
        this.rpcWaiters.TryAdd(messageId, rpcWaiter);
        await this.channel.Writer.WriteAsync(new Message
        {
            MessageId = messageId,
            ReplyTo = $"rpc.result.{this.ServiceId}",
            Type = Consts.RpcMessage,
            TraceId = traceId,
            Exchange = exchange,
            RoutingKey = routingKey,
            Body = request
        }, cancellationToken);

        //Console.WriteLine($"RpcMessage,Request Message, MessageId: {theaMessage.MessageId}, From:{this.ServiceId}, DateTime: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        var response = await rpcWaiter.Waiter.WaitAsync(TimeSpan.FromSeconds(timeoutSeconds), exMessage, cancellationToken);
        if (response.Type == Consts.RpcFailure)
            throw new Exception(response.Body);
        return response.Body.JsonTo<TResponse>();
    }
    public async Task ScheduleAsync<TMessage>(string exchange, string routingKey, TMessage message, DateTime enqueueTimeUtc, CancellationToken cancellationToken = default)
    {
        if (enqueueTimeUtc < DateTime.UtcNow)
            throw new Exception($"入队时间晚于现在时间，只能选择未来时间");
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
    public void UseTransfer(string fromExchange, string toExchange, string routingKey)
    {
        if (this.exchangeTransfers.Exists(f => f.FromExchange == fromExchange && f.ToExchange == toExchange))
            return;
        this.exchangeTransfers.Add(new ExchangeTransfer
        {
            FromExchange = fromExchange,
            ToExchange = toExchange,
            RoutingKey = routingKey
        });
    }
    public void UseStatefulConsumer(string exchange, string queue, MethodInfo methodInfo, bool isSingleActiveConsumer = true, bool isQuorumQueue = true)
    {
        if (methodInfo == null)
            throw new ArgumentNullException(nameof(methodInfo));

        this.hasConsumer = true;
        if (!this.consumerHandlers.TryGetValue(queue, out var exchangeHandlers))
            this.consumerHandlers.TryAdd(queue, exchangeHandlers = new());
        exchangeHandlers.TryAdd(exchange, methodInfo);

        if (this.queues.Exists(f => f.QueueId == queue))
        {
            var myBinding = this.bindings.Find(f => f.QueueId == queue);
            throw new Exception($"队列{queue}已添加，有状态队列只能绑定一次，已绑定交换机: {myBinding.ExchangeId}");
        }

        this.queues.Add(new Queue
        {
            QueueId = queue,
            AppId = this.AppId,
            IsQuorumQueue = isQuorumQueue,
            IsStateful = true,
            IsSingleActiveConsumer = isSingleActiveConsumer,
            PrefetchCount = 250,
            WorkloadTotal = 2,
            IsEnabled = true,
            IsLogEnabled = false
        });
        this.bindings.Add(new Binding
        {
            ExchangeId = exchange,
            QueueId = queue,
            BindType = Consts.TopicBindingType,
            IsDelay = false
        });
    }
    public void UseSubscriber(string queue, MethodInfo methodInfo, bool isQuorumQueue = true)
    {
        if (methodInfo == null)
            throw new ArgumentNullException(nameof(methodInfo));

        this.hasConsumer = true;
        if (!this.consumerHandlers.TryGetValue(queue, out var exchangeHandlers))
            this.consumerHandlers.TryAdd(queue, exchangeHandlers = new());
        exchangeHandlers.TryAdd(string.Empty, methodInfo);

        //无状态队列，不同的队列不同的消费者，根据不同的routingKey路由到不同的队列中
        if (!this.queues.Exists(f => f.QueueId == queue))
        {
            this.queues.Add(new Queue
            {
                QueueId = queue,
                AppId = this.AppId,
                IsQuorumQueue = isQuorumQueue,
                IsStateful = false,
                IsSingleActiveConsumer = false,
                PrefetchCount = 250,
                WorkloadTotal = 2,
                IsEnabled = true,
                IsLogEnabled = false
            });
        }
    }
    public void UseSubscriber(string exchange, string queue, MethodInfo methodInfo, bool isDelay = false, bool isQuorumQueue = true)
    {
        if (methodInfo == null)
            throw new ArgumentNullException(nameof(methodInfo));

        this.hasConsumer = true;
        if (!this.consumerHandlers.TryGetValue(queue, out var exchangeHandlers))
            this.consumerHandlers.TryAdd(queue, exchangeHandlers = new());
        exchangeHandlers.TryAdd(exchange, methodInfo);

        //无状态队列，不同的队列不同的消费者，根据不同的routingKey路由到不同的队列中
        var bindingType = isDelay ? Consts.DelayBindingType : Consts.TopicBindingType;

        if (this.queues.Exists(f => f.QueueId == queue && f.IsStateful))
        {
            var myBinding = this.bindings.Find(f => f.QueueId == queue);
            throw new Exception($"队列{queue}已添加，有状态队列只能绑定一次，已绑定交换机: {myBinding.ExchangeId}");
        }
        if (!this.queues.Exists(f => f.QueueId == queue))
        {
            this.queues.Add(new Queue
            {
                QueueId = queue,
                AppId = this.AppId,
                IsQuorumQueue = isQuorumQueue,
                IsStateful = false,
                IsSingleActiveConsumer = false,
                PrefetchCount = 250,
                WorkloadTotal = 2,
                IsEnabled = true,
                IsLogEnabled = false
            });
        }
        if (!this.bindings.Exists(f => f.ExchangeId == exchange && f.QueueId == queue))
        {
            this.bindings.Add(new Binding
            {
                ExchangeId = exchange,
                QueueId = queue,
                BindType = bindingType,
                BindingKey = Consts.DirectRoutingKey,
                IsDelay = isDelay
            });
        }
    }
    public void UseRpcConsumer() => this.isRpcConsumer = true;
    public async Task Change(string queue, int workloadTotal, int? prefetchCount = null, bool? isLogEnabled = null)
    {
        var myQueue = this.queues.Find(f => f.QueueId == queue);
        if (myQueue == null || !myQueue.IsEnabled)
            return;
        if (prefetchCount.HasValue)
            myQueue.PrefetchCount = prefetchCount.Value;
        if (isLogEnabled.HasValue)
            myQueue.IsLogEnabled = isLogEnabled.Value;

        if (myQueue.IsStateful && workloadTotal > myQueue.WorkloadTotal)
        {
            for (int i = myQueue.WorkloadTotal; i < workloadTotal; i++)
            {
                var queueName = $"{queue}.{i}";
                if (this.configInfo.IsAllowCreateQueue)
                    await this.rabbitProducer.CreateQueue(queueName, myQueue.IsQuorumQueue, myQueue.IsSingleActiveConsumer, false);

                if (this.configInfo.IsAllowCreateBinding)
                {
                    var exchanges = this.bindings.Where(f => f.QueueId == queue).Select(f => f.ExchangeId).ToList();
                    foreach (var exchange in exchanges)
                        await this.rabbitProducer.BindQueue(exchange, queueName, i.ToString());
                }
            }
        }
        myQueue.WorkloadTotal = workloadTotal;
        await this.repository.Change(myQueue);
    }
    public async Task RemoveQueue(string queue, int minIndex, int maxIndex)
    {
        for (int i = minIndex; i <= maxIndex; i++)
        {
            var queueName = $"{queue}.{i}";
            await this.rabbitProducer.RemoveQueue(queueName);
        }
    }
    internal void UseRepository(IMessageDrivenRepository repository) => this.repository = repository;
    internal async Task ProcessMessage(Message message)
    {
        //心跳消息单独处理，不参与用户业务排队
        if (message.Type == Consts.Heartbeat)
        {
            var serviceId = message.Body as string;
            this.heartbeats[serviceId] = DateTime.UtcNow;
        }
        else await this.channel.Writer.WriteAsync(message);
    }
    internal void SetRpcResult(string messageId, RpcResponse response)
    {
        if (this.rpcWaiters.TryRemove(messageId, out var rpcWaiter))
            rpcWaiter.Waiter.TrySetResult(response);
    }
    private async Task Register()
    {
        //捞取数据库或是配置中心的集群信息
        await this.repository.Register(this.queues, this.bindings);
        this.localQueueStates = this.queues.ToDictionary(f => f.QueueId, f => new QueueState
        {
            IsQuorumQueue = f.IsQuorumQueue,
            IsSingleActiveConsumer = f.IsSingleActiveConsumer
        });
        (this.queues, this.bindings) = await this.repository.GetSettings(false);

        //创建交换机和队列及绑定
        string queueName = null;
        this.rabbitProducer = await RabbitProducer.CreateAsync(this, this.serviceProvider);
        //创建RPC消费者
        if (this.isRpcConsumer)
        {
            if (!this.configInfo.IsAllowCreateQueue)
                throw new Exception("未配置允许创建队列，无法创建RPC结果队列，请检查配置项：IsAllowCreateQueue");

            queueName = $"{Consts.RpcExchange}.{this.AppId}.{this.ServiceId}";
            this.rpcConsumer = new RabbitConsumer(queueName, queueName, this, this.serviceProvider, QueueType.RpcResult);
            await this.rpcConsumer.Start();
        }
        //创建队列和绑定，需要捞取数据库或是配置中心的最新信息
        //包含所有应用的队列和绑定信息，交换机可以多个应用公用，每个应用有自己的队列及处理程序
        if (!this.hasConsumer) return;

        //一个有状态业务就是一个交换机，多个无状态队列可以订阅同一个交换机
        //检查有状态队列的交换机只能一个，防止多个同一个交换机不同的工作负载无法实现负载均衡
        var queueIds = this.queues.Where(f => f.IsStateful).Select(f => f.QueueId).ToList();
        var exchangeQueues = this.bindings.Where(f => queueIds.Contains(f.QueueId)).GroupBy(f => f.ExchangeId)
            .ToDictionary(f => f.Key, f => f.Select(t => t.QueueId).Distinct().ToList())
            .Where(f => f.Value.Count > 1).ToList();
        foreach (var exchangeQueue in exchangeQueues)
            throw new Exception($"与交换机{exchangeQueue.Key}绑定的有状态队列只能一个，目前绑定的有状态队列[{string.Join(',', exchangeQueue.Value)}]");

        //检查交换机绑定的队列只能使用一种绑定类型，防止同一个交换机绑定的队列使用不同的绑定类型无法创建交换机
        var exchangeBindings = this.bindings.GroupBy(f => f.ExchangeId).ToDictionary(f => f.Key, f =>
            f.Select(t => t.BindType).Distinct().ToList()).Where(f => f.Value.Count > 1).ToList();
        foreach (var myExchangeBinding in exchangeBindings)
            throw new Exception($"交换机{myExchangeBinding.Key}使用了多个不同的绑定类型{string.Join(",", myExchangeBinding.Value)}");

        var queueApps = this.queues.GroupBy(f => f.QueueId).ToDictionary(f => f.Key, f => f.Select(t => t.AppId)
            .Distinct().ToList()).Where(f => f.Value.Count > 1).ToList();
        foreach (var queueApp in queueApps)
            throw new Exception($"同一个队列只能一个应用消费，Queue: {queueApp.Key}, AppId: {string.Join(",", queueApp.Value)}");

        //创建交换机
        if (this.configInfo.IsAllowCreateExchange)
        {
            //工作交换机
            var myBindings = this.bindings.Select(f => new { f.ExchangeId, f.BindType, f.IsDelay }).Distinct().ToList();
            foreach (var myBinding in myBindings)
                await this.rabbitProducer.CreateExchange(myBinding.ExchangeId, myBinding.BindType, myBinding.IsDelay);

            //转发交换机
            var exchangeName = $"{Consts.TransferExchange}.{this.AppId}";
            await this.rabbitProducer.CreateExchange(exchangeName, Consts.TopicBindingType);
            //心跳交换机
            await this.rabbitProducer.CreateExchange(Consts.HeartbeatExchange, Consts.TopicBindingType);
            //RPC交换机
            if (this.isRpcConsumer)
                await this.rabbitProducer.CreateExchange(Consts.RpcExchange, Consts.TopicBindingType);
        }

        //创建队列
        var myQueues = this.queues.FindAll(f => f.AppId == this.AppId);
        if (this.configInfo.IsAllowCreateQueue)
        {
            //创建工作负载队列            
            foreach (var myQueue in myQueues)
            {
                if (myQueue.IsStateful)
                {
                    //数据库配置的负载个数为准，创建有状态队列
                    for (int i = 0; i < myQueue.WorkloadTotal; i++)
                    {
                        queueName = $"{myQueue.QueueId}.{i}";
                        await this.rabbitProducer.CreateQueue(queueName, myQueue.IsQuorumQueue, myQueue.IsSingleActiveConsumer, false);
                    }
                }
                else await this.rabbitProducer.CreateQueue(myQueue.QueueId, myQueue.IsQuorumQueue, false, false);
            }

            //创建转发队列
            queueName = $"{Consts.TransferExchange}.{this.AppId}";
            await this.rabbitProducer.CreateQueue(queueName, true, true, false);

            //创建心跳队列
            queueName = $"{Consts.HeartbeatExchange}.{this.AppId}.{this.ServiceId}";
            this.heartbeatConsumer = new RabbitConsumer(queueName, queueName, this, this.serviceProvider, QueueType.Heartbeat);
            await this.heartbeatConsumer.Start();
        }

        //创建队列绑定        
        if (this.configInfo.IsAllowCreateBinding)
        {
            //创建工作负载队列绑定
            foreach (var myQueue in myQueues)
            {
                var myBindings = this.bindings.Where(f => f.QueueId == myQueue.QueueId).ToList();
                if (myQueue.IsStateful)
                {
                    for (int i = 0; i < myQueue.WorkloadTotal; i++)
                    {
                        foreach (var myBinding in myBindings)
                        {
                            queueName = $"{myQueue.QueueId}.{i}";
                            await this.rabbitProducer.BindQueue(myBinding.ExchangeId, queueName, i.ToString());
                        }
                    }
                }
                else await this.rabbitProducer.BindQueue(myBindings[0].ExchangeId, myQueue.QueueId, Consts.DirectRoutingKey);
            }
            //创建转发队列绑定
            queueName = $"{Consts.TransferExchange}.{this.AppId}";
            await this.rabbitProducer.BindQueue(queueName, queueName, Consts.DirectRoutingKey);

            //创建心跳队列绑定
            queueName = $"{Consts.HeartbeatExchange}.{this.AppId}.{this.ServiceId}";
            await this.rabbitProducer.BindQueue(Consts.HeartbeatExchange, queueName, this.AppId);

            //创建交换机转发绑定
            foreach (var transfer in this.exchangeTransfers)
                await this.rabbitProducer.BindExchange(transfer.FromExchange, transfer.ToExchange, transfer.RoutingKey);
        }
        this.lastQueues = this.queues;
    }
    private async Task Initialize()
    {
        //发送心跳消息
        await this.rabbitProducer.PublishAsync(Consts.HeartbeatExchange, this.AppId, new BasicProperties
        {
            Persistent = false,
            Type = Consts.Heartbeat,
            DeliveryMode = DeliveryModes.Transient,
            AppId = this.AppId,
            MessageId = ObjectId.NewId()
        }, this.ServiceId);
        (this.queues, this.bindings) = await this.repository.GetSettings();
        foreach (var myQueue in this.queues)
        {
            if (!this.localQueueStates.TryGetValue(myQueue.QueueId, out var queueState))
                continue;
            myQueue.IsQuorumQueue = queueState.IsQuorumQueue;
            myQueue.IsSingleActiveConsumer = queueState.IsSingleActiveConsumer;
        }
        if (!this.hasConsumer) return;

        //启动消费者
        var removedKeys = this.heartbeats
           .Where(f => f.Key != this.ServiceId && DateTime.UtcNow.Subtract(f.Value) > this.heartbeatCycle * 3)
           .Select(f => f.Key).ToList();
        if (removedKeys.Count > 0)
            removedKeys.ForEach(f => this.heartbeats.TryRemove(f, out _));

        //启动消费者
        var nodeIds = this.heartbeats.Keys.ToList();
        nodeIds.Sort((x, y) => x.CompareTo(y));
        var currentNodeIds = string.Join(",", nodeIds);
        if (currentNodeIds != this.lastNodeIds)
            Console.WriteLine($"可用节点：{currentNodeIds}");

        int index = 0;
        List<RabbitConsumer> rabbitConsumers = null;
        var consumerIds = new Dictionary<string, List<string>>();

        //先创建有状态队列消费者(包括SAC和非SAC消费者)
        var myQueues = this.queues.Where(f => f.AppId == this.AppId && f.IsEnabled
            && f.IsStateful).OrderBy(f => f.QueueId).ToList();

        //创建新增的队列和绑定
        foreach (var lastQueue in this.lastQueues)
        {
            if (lastQueue.AppId != this.AppId) continue;

            var lastWorkloadTotal = lastQueue.WorkloadTotal;
            var myQueue = myQueues.Find(f => f.QueueId == lastQueue.QueueId);
            if (myQueue == null || lastWorkloadTotal >= myQueue.WorkloadTotal)
                continue;

            var myBindings = this.bindings.FindAll(f => f.QueueId == lastQueue.QueueId);
            for (int i = lastWorkloadTotal; i < myQueue.WorkloadTotal; i++)
            {
                var queueName = $"{myQueue.QueueId}.{i}";
                if (this.configInfo.IsAllowCreateQueue)
                    await this.rabbitProducer.CreateQueue(queueName, myQueue.IsQuorumQueue, myQueue.IsSingleActiveConsumer, false);
                if (this.configInfo.IsAllowCreateBinding)
                {
                    foreach (var myBinding in myBindings)
                        await this.rabbitProducer.BindQueue(myBinding.ExchangeId, queueName, i.ToString());
                }
            }
        }

        //创建有状态队列消费者
        foreach (var myQueue in myQueues)
        {
            for (int i = 0; i < myQueue.WorkloadTotal; i++)
            {
                var queueName = $"{myQueue.QueueId}.{i}";
                var consumerId = $"{queueName}.{this.ServiceId}.0";
                await this.CreateConsumer(index, myQueue, queueName, consumerId, nodeIds, consumerIds);
                index++;
            }
        }

        //创建无状态队列消费者
        myQueues = this.queues.Where(f => f.AppId == this.AppId && !f.IsStateful).OrderBy(f => f.QueueId).ToList();
        foreach (var myQueue in myQueues)
        {
            var queueName = myQueue.QueueId;
            for (int i = 0; i < myQueue.WorkloadTotal; i++)
            {
                var consumerId = $"{queueName}.{this.ServiceId}.{i}";
                await this.CreateConsumer(index, myQueue, queueName, consumerId, nodeIds, consumerIds);
                index++;
            }
        }

        //创建有状态队列SAC等待消费者
        myQueues = this.queues.Where(f => f.AppId == this.AppId && f.IsStateful && f.IsSingleActiveConsumer).OrderBy(f => f.QueueId).ToList();
        foreach (var myQueue in myQueues)
        {
            for (int i = 0; i < myQueue.WorkloadTotal; i++)
            {
                for (int workloadIndex = 1; workloadIndex < this.configInfo.SacCount; workloadIndex++)
                {
                    var queueName = $"{myQueue.QueueId}.{i}";
                    var consumerId = $"{queueName}.{this.ServiceId}.{workloadIndex}";
                    await this.CreateConsumer(index, myQueue, queueName, consumerId, nodeIds, consumerIds);
                    index++;
                }
            }
        }

        //创建转发队列
        for (int workloadIndex = 0; workloadIndex < this.configInfo.SacCount; workloadIndex++)
        {
            await this.CreateTransferConsumer(workloadIndex, nodeIds, consumerIds);
            index++;
        }
        this.lastNodeIds = currentNodeIds;

        //最后处理多余的消费者
        var queueNames = this.consumers.Keys.ToList();
        foreach (var myQueueName in queueNames)
        {
            if (!this.consumers.TryGetValue(myQueueName, out rabbitConsumers))
                continue;
            if (rabbitConsumers == null || rabbitConsumers.Count == 0)
            {
                this.consumers.TryRemove(myQueueName, out _);
                continue;
            }
            if (consumerIds.TryGetValue(myQueueName, out var myConsumerIds))
            {
                //队列存在，删除不是本节点的消费者
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
            else
            {
                //不应该存在的队列，等待队列没有消息后，再过3个心跳周期，删除所有消费者
                var hasMessage = false;
                foreach (var myConsumer in rabbitConsumers)
                {
                    if (await myConsumer.MessageCount() > 0)
                    {
                        hasMessage = true;
                        break;
                    }
                }
                if (hasMessage) continue;

                //过2个心跳周期，再删除消费者
                if (!this.shutdownQueues.TryGetValue(myQueueName, out var lastUpdateTime))
                    this.shutdownQueues.TryAdd(myQueueName, lastUpdateTime = DateTime.UtcNow);
                if (DateTime.UtcNow.Subtract(lastUpdateTime) < this.heartbeatCycle * 2)
                    continue;
                this.shutdownQueues.TryRemove(myQueueName, out _);
                foreach (var myConsumer in rabbitConsumers)
                    await myConsumer.Shutdown(true);
                rabbitConsumers.Clear();
                this.consumers.TryRemove(myQueueName, out _);
                Console.WriteLine($"队列{myQueueName}所有消费者已关闭");
            }
        }
    }
    async Task CreateConsumer(int index, Queue myQueue, string queueName,
        string consumerId, List<string> nodeIds, Dictionary<string, List<string>> consumerIds)
    {
        var nodeId = nodeIds.Count > 1 ? nodeIds[index % nodeIds.Count] : this.ServiceId;
        if (nodeId != this.ServiceId) return;

        var rabbitConsumers = this.consumers.GetOrAdd(queueName, f => new List<RabbitConsumer>());
        if (!consumerIds.TryGetValue(queueName, out var myConsumerIds))
            consumerIds.TryAdd(queueName, myConsumerIds = new());
        myConsumerIds.Add(consumerId);
        var myRabbitConsumer = rabbitConsumers.Find(f => f.ConsumerId == consumerId);
        if (myRabbitConsumer != null)
        {
            myRabbitConsumer.IsLogEnabled = myQueue.IsLogEnabled;
            if (!myRabbitConsumer.IsActivated || myRabbitConsumer.PrefetchCount != myQueue.PrefetchCount)
            {
                myRabbitConsumer.PrefetchCount = myQueue.PrefetchCount;
                await myRabbitConsumer.Shutdown(true);
                await myRabbitConsumer.Start();
            }
        }
        else
        {
            //新增消费者，直接启动
            var exchangeHandlers = this.consumerHandlers[queueName];
            myRabbitConsumer = new RabbitConsumer(queueName, consumerId, this, this.serviceProvider, QueueType.Message, myQueue.PrefetchCount, exchangeHandlers) { IsLogEnabled = myQueue.IsLogEnabled };
            rabbitConsumers.Add(myRabbitConsumer);
            await myRabbitConsumer.Start();
            var queueType = myQueue.IsStateful ? "有" : "无";
            Console.WriteLine($"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}, {queueType}状态队列{queueName} 消费者{consumerId}已启动，当前消费者数量：{rabbitConsumers.Count}");
        }
    }
    async Task CreateTransferConsumer(int index, List<string> nodeIds, Dictionary<string, List<string>> consumerIds)
    {
        var nodeId = nodeIds.Count > 1 ? nodeIds[index % nodeIds.Count] : this.ServiceId;
        if (nodeId != this.ServiceId) return;

        var queueName = $"{Consts.TransferExchange}.{this.AppId}";
        var rabbitConsumers = this.consumers.GetOrAdd(queueName, f => new List<RabbitConsumer>());
        if (!consumerIds.TryGetValue(queueName, out var myConsumerIds))
            consumerIds.TryAdd(queueName, myConsumerIds = new());
        myConsumerIds.Add(queueName);
        var myRabbitConsumer = rabbitConsumers.Find(f => f.ConsumerId == queueName);
        if (myRabbitConsumer != null)
        {
            if (!myRabbitConsumer.IsActivated)
            {
                await myRabbitConsumer.Shutdown(true);
                await myRabbitConsumer.Start();
            }
        }
        else
        {
            //新增消费者，直接启动
            myRabbitConsumer = new RabbitConsumer(queueName, $"{queueName}.{this.ServiceId}.{index}", this, this.serviceProvider, QueueType.Transfer);
            rabbitConsumers.Add(myRabbitConsumer);
            await myRabbitConsumer.Start();
            Console.WriteLine($"转发队列消费者{queueName}已启动，当前消费者数量：{rabbitConsumers.Count}");
        }
    }
}