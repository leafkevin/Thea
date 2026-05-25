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
    private readonly Channel<Message> channel = Channel.CreateBounded<Message>(new BoundedChannelOptions(5000)
    {
        FullMode = BoundedChannelFullMode.Wait,
        SingleWriter = false,
        SingleReader = true
    });
    private bool hasProducer = false;
    private bool hasConsumer = false;
    private string lastNodeIds = null;
    private bool isRpcConsumer = false;
    private bool isNeedTransfer = false;

    private List<Binding> bindings = new();
    private List<Setting> settings = new();

    private ConfigInfo configInfo = new();
    private RabbitConsumer heartbeatConsumer;
    private RabbitConsumer rpcConsumer;

    private readonly Dictionary<string, Dictionary<string, MethodInfo>> consumerHandlers = new();
    private readonly IServiceProvider serviceProvider;
    private readonly ILogger<MessageDrivenService> logger;
    private IMessageDrivenRepository repository;
    private DateTime lastInitedTime = DateTime.MinValue;
    private DateTime lastLoggedTime = DateTime.MinValue;

    internal RabbitProducer rabbitProducer;
    internal List<AmqpTcpEndpoint> tcpEndPoints;
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
                    if (this.rpcWaiters.IsEmpty)
                    {
                        var messageIds = this.rpcWaiters.Keys.ToList();
                        foreach (var messageId in messageIds)
                        {
                            var rpcWaiter = this.rpcWaiters[messageId];
                            var elapsedSeconds = DateTime.UtcNow.Subtract(rpcWaiter.CreatedAt).TotalSeconds - rpcWaiter.TimeoutSeconds;
                            if (elapsedSeconds < rpcWaiter.TimeoutSeconds)
                                continue;
                            this.rpcWaiters.TryRemove(messageId, out _);
                            //只结束超时的RPC请求，不做清理工作，清理工作在Request方法中处理
                            rpcWaiter.Waiter.TrySetException(new TimeoutException(rpcWaiter.TimeoutMessage));
                        }
                    }
                    for (int i = 0; i < 10; i++)
                    {
                        if (!this.channel.Reader.TryRead(out message))
                            break;
                        string queueId = null;
                        string queueName = null;
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
                                else
                                {
                                    if (message.Type == Consts.RpcMessage)
                                    {
                                        //RPC消息，设置回复队列和过期时间
                                        properties.ReplyTo = message.ReplyTo;
                                        properties.CorrelationId = message.MessageId;
                                    }

                                    if (this.hasConsumer)
                                    {
                                        var workloadTotals = this.settings.Where(f => f.Exchanges.Contains(message.Exchange) && f.IsStateful)
                                            .Select(f => f.WorkloadTotal).Distinct().ToList();

                                        //如果存在有状态队列，根据消息的RoutingKey进行一致性哈希，路由到对应的队列中
                                        if (workloadTotals.Count > 0)
                                        {
                                            if (workloadTotals.Count > 1)
                                            {
                                                var exMessage = $"交换机{message.Exchange}绑定的有状态队列工作负载个数不一致，无法实现消息负载均衡，请检查配置";
                                                this.logger.LogTagError("MessageDriven", new Exception(exMessage), exMessage);
                                                Console.WriteLine(exMessage);
                                                throw new Exception(exMessage);
                                            }
                                            var routingKey = JumpConsistentHash.GetBucket(message.RoutingKey, workloadTotals[0]);
                                            message.RoutingKey = routingKey.ToString();
                                            await this.rabbitProducer.PublishAsync(message.Exchange, routingKey.ToString(), properties, message.Body.ToJson());
                                        }
                                        //如果没有有状态队列，直接路由到交换机，由交换机根据绑定规则路由到对应的队列中
                                        else await this.rabbitProducer.PublishAsync(message.Exchange, message.RoutingKey, properties, message.Body.ToJson());
                                    }
                                    //如果当前应用没有对应的消费者，消息会发送到其他应用的交换机，直接发送不做任何处理
                                    //有状态消息时，exhcange是其他应用的交换机，$"Consts.TransferQueue.{this.AppId}"，如：transfer.GameConsumer
                                    //无状态消息时，exchange直接就是消息的交换机，如：player
                                    else await this.rabbitProducer.PublishAsync(message.Exchange, message.RoutingKey, properties, message.Body.ToJson());
                                }
                                //防止条件问题阻塞后续消费
                                message.Waiter?.TrySetResult(true);
                                break;
                            case Consts.Heartbeat:
                                //用户消息堆积，心跳消息也会阻塞，此节点会被认为是异常节点，将会从可用节点中移除
                                var nodeId = (string)message.Body;
                                this.heartbeats.AddOrUpdate(nodeId, DateTime.UtcNow, (k, o) => DateTime.UtcNow);
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
        if (!this.hasProducer && !this.hasConsumer)
            throw new Exception("未配置生产者或是消费者，请至少使用UseProducer或是UseStatefulConsumer、UseSubscriber方法进行配置");

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
        var rpcMessage = await rpcWaiter.Waiter.WaitAsync(TimeSpan.FromSeconds(timeoutSeconds), exMessage, cancellationToken);
        if (rpcMessage.Type == Consts.RpcFailure)
            throw new Exception(rpcMessage.Body);
        return rpcMessage.Body.JsonTo<TResponse>();
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
    public void UseBinding(string fromExchange, string toExchange, string routingKey)
    {
        if (this.bindings.Exists(f => f.FromExchange == fromExchange && f.ToExchange == toExchange))
            return;
        this.bindings.Add(new Binding
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
        var myQueue = this.settings.Find(f => f.Queue == queue);
        if (myQueue == null)
        {
            this.settings.Add(myQueue = new Setting
            {
                Queue = queue,
                BindType = Consts.TopicBindingType,
                IsQuorumQueue = isQuorumQueue,
                IsStateful = true,
                IsSingleActiveConsumer = isSingleActiveConsumer,
                IsDelay = false,
                PrefetchCount = 250,
                WorkloadTotal = 2,
                Exchanges = [exchange],
                IsEnabled = true,
                IsLogEnabled = false,
                CreatedBy = this.AppId,
                CreatedAt = DateTime.UtcNow,
                UpdatedBy = this.AppId,
                UpdatedAt = DateTime.UtcNow
            });
        }
        else if (!myQueue.Exchanges.Contains(exchange))
            myQueue.Exchanges.Add(exchange);
    }
    public void UseSubscriber(string queue, MethodInfo methodInfo, bool isDelay = false, bool isQuorumQueue = true)
    {
        if (methodInfo == null)
            throw new ArgumentNullException(nameof(methodInfo));

        this.hasConsumer = true;
        if (!this.consumerHandlers.TryGetValue(queue, out var exchangeHandlers))
            this.consumerHandlers.TryAdd(queue, exchangeHandlers = new());
        exchangeHandlers.TryAdd(string.Empty, methodInfo);

        //无状态队列，不同的队列不同的消费者，根据不同的routingKey路由到不同的队列中
        var bindingType = isDelay ? Consts.DelayBindingType : Consts.TopicBindingType;
        var myQueue = this.settings.Find(f => f.Queue == queue);
        if (myQueue == null)
        {
            this.settings.Add(myQueue = new Setting
            {
                Queue = queue,
                BindType = bindingType,
                BindingKey = Consts.DirectRoutingKey,
                IsQuorumQueue = isQuorumQueue,
                IsStateful = false,
                IsSingleActiveConsumer = false,
                IsDelay = isDelay,
                PrefetchCount = 250,
                WorkloadTotal = 2,
                Exchanges = [string.Empty],
                IsEnabled = true,
                IsLogEnabled = false,
                CreatedBy = this.AppId,
                CreatedAt = DateTime.UtcNow,
                UpdatedBy = this.AppId,
                UpdatedAt = DateTime.UtcNow
            });
        }
        else if (!myQueue.Exchanges.Contains(string.Empty))
            myQueue.Exchanges.Add(string.Empty);
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
        var myQueue = this.settings.Find(f => f.Queue == queue);
        if (myQueue == null)
        {
            this.settings.Add(myQueue = new Setting
            {
                Queue = queue,
                BindType = bindingType,
                BindingKey = Consts.DirectRoutingKey,
                IsQuorumQueue = isQuorumQueue,
                IsStateful = false,
                IsSingleActiveConsumer = false,
                IsDelay = isDelay,
                PrefetchCount = 250,
                WorkloadTotal = 2,
                Exchanges = [exchange],
                IsEnabled = true,
                IsLogEnabled = false,
                CreatedBy = this.AppId,
                CreatedAt = DateTime.UtcNow,
                UpdatedBy = this.AppId,
                UpdatedAt = DateTime.UtcNow
            });
        }
        else if (!myQueue.Exchanges.Contains(exchange))
            myQueue.Exchanges.Add(exchange);
    }
    public void UseTransfer() => this.isNeedTransfer = true;
    public void UseRpcConsumer() => this.isRpcConsumer = true;
    public async Task Change(string queue, int workloadTotal, int? prefetchCount = null, bool? isLogEnabled = null)
    {
        var mySetting = this.settings.Find(f => f.Queue == queue);
        if (mySetting == null || !mySetting.IsEnabled)
            return;

        if (mySetting.WorkloadTotal == workloadTotal)
            return;

        if (mySetting.IsStateful && workloadTotal > mySetting.WorkloadTotal)
        {
            for (int i = mySetting.WorkloadTotal; i < workloadTotal; i++)
            {
                var queueName = $"{queue}.{i}";
                if (this.configInfo.IsAllowCreateQueue)
                    await this.rabbitProducer.CreateQueue(queueName, mySetting.IsQuorumQueue, mySetting.IsSingleActiveConsumer, false);
                if (this.configInfo.IsAllowCreateBinding)
                {
                    foreach (var exchange in mySetting.Exchanges)
                    {
                        await this.rabbitProducer.BindQueue(exchange, queueName, i.ToString());
                    }
                }
            }
        }
        mySetting.WorkloadTotal = workloadTotal;
        await this.repository.Change(queue, workloadTotal);
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
    internal async Task ProcessMessage(Message message) => await this.channel.Writer.WriteAsync(message);
    internal void SetRpcResult(string messageId, Message<string> result)
    {
        if (this.rpcWaiters.TryRemove(messageId, out var rpcWaiter))
            rpcWaiter.Waiter.TrySetResult(result);
    }
    private async Task Register()
    {
        //捞取数据库或是配置中心的集群信息
        await this.repository.Register(this.settings);
        var dbSettings = await this.repository.GetSettings(false);

        //创建交换机和队列及绑定
        string queueName = null;
        if (this.hasProducer) this.rabbitProducer = await RabbitProducer.CreateAsync(this, this.serviceProvider);
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
        if (!this.hasConsumer) return;

        if (this.configInfo.IsAllowCreateExchange)
        {
            foreach (var mySetting in this.settings)
            {
                foreach (var exchange in mySetting.Exchanges)
                {
                    await this.rabbitProducer.CreateExchange(exchange, mySetting.BindType, mySetting.IsDelay);
                }
            }
            var exchangeName = $"{Consts.TransferQueue}.{this.AppId}";
            await this.rabbitProducer.CreateExchange(exchangeName, Consts.TopicBindingType);
        }
        if (this.configInfo.IsAllowCreateQueue)
        {
            //创建工作负载队列
            foreach (var mySetting in this.settings)
            {
                var setting = dbSettings.Find(f => f.Queue == mySetting.Queue);
                if (setting != null)
                {
                    //如果数据库中存在配置，就使用数据库的配置
                    mySetting.WorkloadTotal = setting.WorkloadTotal;
                    mySetting.IsEnabled = setting.IsEnabled;
                    mySetting.IsLogEnabled = setting.IsLogEnabled;
                }
                if (mySetting.IsStateful)
                {
                    //数据库配置的负载个数为准，创建有状态队列
                    for (int i = 0; i < mySetting.WorkloadTotal; i++)
                    {
                        queueName = $"{mySetting.Queue}.{i}";
                        await this.rabbitProducer.CreateQueue(queueName, mySetting.IsQuorumQueue, mySetting.IsSingleActiveConsumer, false);
                    }
                }
                else await this.rabbitProducer.CreateQueue(mySetting.Queue, mySetting.IsQuorumQueue, false, false);
            }

            //创建转发队列
            if (this.isNeedTransfer)
            {
                queueName = $"{Consts.TransferQueue}.{this.AppId}";
                var mySetting = this.settings.Find(f => f.Queue == queueName);
                if (mySetting.WorkloadTotal > 1)
                {
                    for (int i = 0; i < mySetting.WorkloadTotal; i++)
                    {
                        queueName = $"{Consts.TransferQueue}.{this.AppId}.{i}";
                        await this.rabbitProducer.CreateQueue(queueName, true, false, false);
                    }
                }
                else await this.rabbitProducer.CreateQueue(queueName, true, false, false);
            }

            //创建心跳队列
            queueName = $"heartbeat.{this.AppId}.{this.ServiceId}";
            this.heartbeatConsumer = new RabbitConsumer(queueName, queueName, this, this.serviceProvider, QueueType.Heartbeat);
            await this.heartbeatConsumer.Start();
        }
        if (this.configInfo.IsAllowCreateBinding)
        {
            foreach (var mySetting in this.settings)
            {
                //以数据库配置的负载个数为准，创建有状态队列
                var dbSetting = dbSettings.Find(f => f.Queue == mySetting.Queue);
                var workloadTotal = mySetting?.WorkloadTotal ?? mySetting.WorkloadTotal;

                for (int i = 0; i < workloadTotal; i++)
                {
                    foreach (var exchange in mySetting.Exchanges)
                    {
                        if (mySetting.IsStateful)
                        {
                            queueName = $"{mySetting.Queue}.{i}";
                            await this.rabbitProducer.BindQueue(exchange, queueName, i.ToString());
                        }
                        else await this.rabbitProducer.BindQueue(exchange, mySetting.Queue, Consts.DirectRoutingKey);
                    }
                }
            }
            //创建转发队列绑定
            if (this.isNeedTransfer)
            {
                var exchangeName = $"{Consts.TransferQueue}.{this.AppId}";
                var mySetting = this.settings.Find(f => f.Queue == exchangeName);
                if (mySetting.WorkloadTotal > 1)
                {
                    for (int i = 0; i < mySetting.WorkloadTotal; i++)
                    {
                        queueName = $"{Consts.TransferQueue}.{this.AppId}.{i}";
                        await this.rabbitProducer.BindQueue(exchangeName, queueName, $"{i}");
                    }
                }
                else await this.rabbitProducer.BindQueue(exchangeName, exchangeName, Consts.DirectRoutingKey);
            }
            //创建交换机转发绑定
            if (this.bindings.Count > 0)
            {
                foreach (var binding in this.bindings)
                {
                    await this.rabbitProducer.BindExchange(binding.FromExchange, binding.ToExchange, binding.RoutingKey);
                }
            }
        }
    }
    private async Task Initialize()
    {
        await this.rabbitProducer.PublishAsync(Consts.HeartbeatExchange, this.ServiceId, new BasicProperties
        {
            Persistent = true,
            Type = Consts.Heartbeat,
            DeliveryMode = DeliveryModes.Persistent,
            AppId = this.AppId,
            MessageId = ObjectId.NewId()
        }, this.ServiceId);
        var dbSettings = await this.repository.GetSettings();
        if (!this.hasConsumer)
        {
            //如果只是生产者模式，直接使用数据库的配置，用于发送消息
            this.settings = dbSettings;
            return;
        }

        var transferQueue = $"{Consts.TransferQueue}.{this.AppId}";
        foreach (var mySetting in this.settings)
        {
            var setting = dbSettings.Find(f => f.Queue == mySetting.Queue);
            if (setting == null) continue;

            mySetting.WorkloadTotal = setting.WorkloadTotal;
            mySetting.IsEnabled = setting.IsEnabled;
            mySetting.IsLogEnabled = setting.IsLogEnabled;
        }
        List<Setting> mySettings = null;
        var exchanges = this.settings.Where(f => f.IsStateful).SelectMany(f => f.Exchanges).Distinct().ToList();
        foreach (var exchange in exchanges)
        {
            mySettings = this.settings.Where(f => f.Exchanges.Contains(exchange)).ToList();
            var workloadTotals = mySettings.Select(f => f.WorkloadTotal).Distinct().ToList();
            if (workloadTotals.Count > 1)
            {
                var exMessage = $"交换机{exchange}绑定的有状态队列[{string.Join(",", mySettings.Select(f => f.Queue))}]工作负载不一致，无法实现消息负载均衡，请检查配置";
                this.logger.LogTagError("MessageDriven", new Exception(exMessage), exMessage);
                Console.WriteLine(exMessage);
                throw new Exception(exMessage);
                //避免影响业务，取最小的工作负载个数进行创建和绑定
                //var workloadTotal = workloadTotals.Min();
                //mySettings.ForEach(f => f.WorkloadTotal = workloadTotal);
            }
        }

        var isFirst = string.IsNullOrEmpty(this.lastNodeIds);
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

        //先创建有状态队列消费者(包括SAC和非SAC消费者)
        mySettings = this.settings.Where(f => f.IsStateful && f.Queue != transferQueue).OrderBy(f => f.Queue).ToList();
        //创建新增的队列和绑定
        if (!isFirst)
        {
            foreach (var mySetting in mySettings)
            {
                var dbSetting = dbSettings.Find(f => f.Queue == mySetting.Queue);
                if (!dbSetting.IsEnabled) continue;

                var lastWorkloadTotal = mySetting.WorkloadTotal;
                if (mySetting.WorkloadTotal >= dbSetting.WorkloadTotal)
                    continue;

                for (int i = lastWorkloadTotal; i < dbSetting.WorkloadTotal; i++)
                {
                    var queueName = $"{mySetting.Queue}.{i}";
                    if (this.configInfo.IsAllowCreateQueue)
                        await this.rabbitProducer.CreateQueue(queueName, mySetting.IsQuorumQueue, mySetting.IsSingleActiveConsumer, false);

                    if (this.configInfo.IsAllowCreateBinding)
                    {
                        foreach (var exchange in mySetting.Exchanges)
                        {
                            var routingKey = i.ToString();
                            await this.rabbitProducer.BindQueue(exchange, queueName, routingKey);
                        }
                    }
                }
            }
        }

        int index = 0, nodeCount = nodeIds.Count;
        List<RabbitConsumer> rabbitConsumers = null;
        var consumerIds = new Dictionary<string, List<string>>();

        //先创建有状态队列消费者
        foreach (var mySetting in mySettings)
        {
            var dbSetting = dbSettings.Find(f => f.Queue == mySetting.Queue);
            if (!dbSetting.IsEnabled) continue;

            var lastWorkloadTotal = mySetting.WorkloadTotal;
            for (int i = 0; i < dbSetting.WorkloadTotal; i++)
            {
                var nodeId = nodeCount > 1 ? nodeIds[index % nodeCount] : this.ServiceId;
                index++;
                if (nodeId != this.ServiceId) continue;

                //先创建激活的SAC消费者
                var queueName = $"{mySetting.Queue}.{i}";
                rabbitConsumers = this.consumers.GetOrAdd(queueName, f => new List<RabbitConsumer>());
                //消费者ID带有服务ID，可以展现各个服务中消费者的分布情况，便于平衡各个服务中消费者
                var consumerId = $"{queueName}.{this.ServiceId}.0";
                if (!consumerIds.TryGetValue(queueName, out var myConsumerIds))
                    consumerIds.TryAdd(queueName, myConsumerIds = new());
                myConsumerIds.Add(consumerId);
                var myRabbitConsumer = rabbitConsumers.Find(f => f.ConsumerId == consumerId);
                if (myRabbitConsumer != null)
                {
                    if (!myRabbitConsumer.IsActivated || mySetting.PrefetchCount != dbSetting.PrefetchCount)
                    {
                        await myRabbitConsumer.Shutdown(true);
                        await myRabbitConsumer.Start();
                    }
                    myRabbitConsumer.PrefetchCount = dbSetting.PrefetchCount;
                    myRabbitConsumer.IsLogEnabled = dbSetting.IsLogEnabled;
                    continue;
                }
                //新扩容的队列，还在等待之前队列的消息消费完成，还未启动
                if (this.waitStartingConsumers.TryGetValue(mySetting.Queue, out var existingWaiter)
                    && existingWaiter.Consumers.Exists(f => f.ConsumerId == consumerId))
                    continue;

                if (lastWorkloadTotal > 0 && i > lastWorkloadTotal)
                {
                    var waiter = this.waitStartingConsumers.GetOrAdd(mySetting.Queue, f => new ConsumerWaiter { WaitTotal = lastWorkloadTotal });
                    if (waiter.Consumers.Exists(f => f.ConsumerId == consumerId))
                        continue;
                    var exchangeHandlers = this.consumerHandlers[mySetting.Queue];
                    myRabbitConsumer = new RabbitConsumer(queueName, consumerId, this, this.serviceProvider, QueueType.Message, dbSetting.PrefetchCount, exchangeHandlers);
                    myRabbitConsumer.IsLogEnabled = dbSetting.IsLogEnabled;
                    waiter.Consumers.Add(myRabbitConsumer);
                    Console.WriteLine($"有状态队列消费者{queueName}等待启动，当前等待启动的消费者数量：{waiter.Consumers.Count}");
                }
                else
                {
                    var exchangeHandlers = this.consumerHandlers[mySetting.Queue];
                    myRabbitConsumer = new RabbitConsumer(queueName, consumerId, this, this.serviceProvider, QueueType.Message, dbSetting.PrefetchCount, exchangeHandlers);
                    myRabbitConsumer.IsLogEnabled = dbSetting.IsLogEnabled;
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
                if (lastWorkloadTotal < dbSetting.WorkloadTotal)
                {
                    for (int j = 0; j < lastWorkloadTotal; j++)
                    {
                        var queueName = $"{mySetting.Queue}.{j}";
                        //已经消费完毕的队列名
                        await this.rabbitProducer.PublishAsync(Consts.DefaultExchange, queueName, new BasicProperties
                        {
                            Persistent = true,
                            AppId = this.AppId,
                            Type = Consts.WaitStarting,
                            DeliveryMode = DeliveryModes.Persistent,
                            MessageId = ObjectId.NewId()
                        }, queueName);
                        Console.WriteLine($"扩容队列{lastWorkloadTotal} -> {dbSetting.WorkloadTotal}, 向队列{queueName}发送结束标志消息");
                    }
                }
                else if (lastWorkloadTotal > dbSetting.WorkloadTotal)
                {
                    for (int j = dbSetting.WorkloadTotal; j < lastWorkloadTotal; j++)
                    {
                        var queueName = $"{mySetting.Queue}.{j}";
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
                            Console.WriteLine($"收缩队列{lastWorkloadTotal} -> {dbSetting.WorkloadTotal}, 向队列{queueName}发送结束标志消息");
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

        //再创建无状态队列消费者
        mySettings = this.settings.Where(f => !f.IsStateful && f.Queue != transferQueue).OrderBy(f => f.Queue).ToList();
        foreach (var mySetting in mySettings)
        {
            var dbSetting = dbSettings.Find(f => f.Queue == mySetting.Queue);
            if (!dbSetting.IsEnabled) continue;

            for (int i = 0; i < dbSetting.WorkloadTotal; i++)
            {
                var nodeId = nodeCount > 1 ? nodeIds[index % nodeCount] : this.ServiceId;
                index++;
                if (nodeId != this.ServiceId) continue;

                var consumerId = $"{mySetting.Queue}.{this.ServiceId}.{i}";
                rabbitConsumers = this.consumers.GetOrAdd(mySetting.Queue, f => new List<RabbitConsumer>());
                if (!consumerIds.TryGetValue(mySetting.Queue, out var myConsumerIds))
                    consumerIds.TryAdd(mySetting.Queue, myConsumerIds = new());
                myConsumerIds.Add(consumerId);
                var myRabbitConsumer = rabbitConsumers.Find(f => f.ConsumerId == consumerId);
                if (myRabbitConsumer != null)
                {
                    if (!myRabbitConsumer.IsActivated || mySetting.PrefetchCount != dbSetting.PrefetchCount)
                    {
                        await myRabbitConsumer.Shutdown(true);
                        await myRabbitConsumer.Start();
                    }
                    myRabbitConsumer.PrefetchCount = dbSetting.PrefetchCount;
                    myRabbitConsumer.IsLogEnabled = dbSetting.IsLogEnabled;
                    continue;
                }
                var exchangeHandlers = this.consumerHandlers[mySetting.Queue];
                myRabbitConsumer = new RabbitConsumer(mySetting.Queue, consumerId, this, this.serviceProvider, QueueType.Message, dbSetting.PrefetchCount, exchangeHandlers);
                myRabbitConsumer.IsLogEnabled = mySetting.IsLogEnabled;
                rabbitConsumers.Add(myRabbitConsumer);
                await myRabbitConsumer.Start();
                Console.WriteLine($"无状态队列消费者{consumerId}已启动，当前消费者数量：{rabbitConsumers.Count}");
            }
        }

        //再创建有状态队列SAC等待消费者
        mySettings = this.settings.Where(f => f.IsStateful && f.Queue != transferQueue && f.IsSingleActiveConsumer)
            .OrderBy(f => f.Queue).ToList();
        foreach (var mySetting in mySettings)
        {
            var dbSetting = dbSettings.Find(f => f.Queue == mySetting.Queue);
            if (!dbSetting.IsEnabled) continue;

            var lastWorkloadTotal = mySetting.WorkloadTotal;
            for (int i = 0; i < dbSetting.WorkloadTotal; i++)
            {
                var nodeId = nodeCount > 1 ? nodeIds[index % nodeCount] : this.ServiceId;
                index++;
                //前面队列消费者已经清理过了，不会有多余的，不需要创建直接跳过
                if (nodeId != this.ServiceId) continue;

                var queueName = $"{mySetting.Queue}.{i}";
                rabbitConsumers = this.consumers.GetOrAdd(queueName, f => new List<RabbitConsumer>());
                int workloadIndex = 1;
                while (workloadIndex < this.configInfo.SacCount)
                {
                    var consumerId = $"{queueName}.{this.ServiceId}.{workloadIndex}";
                    if (!consumerIds.TryGetValue(queueName, out var myConsumerIds))
                        consumerIds.TryAdd(queueName, myConsumerIds = new());
                    myConsumerIds.Add(consumerId);
                    workloadIndex++;
                    var myRabbitConsumer = rabbitConsumers.Find(f => f.ConsumerId == consumerId);
                    if (myRabbitConsumer != null)
                    {
                        if (!myRabbitConsumer.IsActivated || mySetting.PrefetchCount != dbSetting.PrefetchCount)
                        {
                            await myRabbitConsumer.Shutdown(true);
                            await myRabbitConsumer.Start();
                        }
                        myRabbitConsumer.PrefetchCount = dbSetting.PrefetchCount;
                        myRabbitConsumer.IsLogEnabled = mySetting.IsLogEnabled;
                        continue;
                    }

                    var exchangeHandlers = this.consumerHandlers[mySetting.Queue];
                    myRabbitConsumer = new RabbitConsumer(queueName, consumerId, this, this.serviceProvider, QueueType.Message, dbSetting.PrefetchCount, exchangeHandlers);
                    myRabbitConsumer.IsLogEnabled = mySetting.IsLogEnabled;
                    if (lastWorkloadTotal > 0 && i > lastWorkloadTotal)
                    {
                        if (!this.waitStartingConsumers.TryGetValue(mySetting.Queue, out var waiter))
                            this.waitStartingConsumers.TryAdd(mySetting.Queue, waiter = new() { WaitTotal = lastWorkloadTotal });
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

        var consumerKeys = this.consumers.Keys.ToList();
        foreach (var queueName in consumerKeys)
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
}