using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Thea.Json;
using Thea.Logging;
using Thea.Orm;

namespace Thea.MessageDriven;

class MessageDrivenService : IMessageDriven
{
    private readonly Task task;
    private readonly TimeSpan cycle = TimeSpan.FromSeconds(30);
    private readonly CancellationTokenSource cancellationSource = new CancellationTokenSource();
    private readonly EventWaitHandle readyToStart = new EventWaitHandle(false, EventResetMode.AutoReset);
    private readonly List<ClusterInfo> localClusterInfos = new();
    private readonly ConcurrentDictionary<int, RabbitProducer> rabbitProducers = new();
    private readonly List<DeferredRemovedConsumer> deferredRemovedConsumers = new();
    private readonly ConcurrentQueue<Message> messageQueue = new();
    private readonly ConcurrentDictionary<string, Cluster> clusters = new();
    //Key=exchang
    private readonly ConcurrentDictionary<string, ProducerInfo> producers = new();
    //Key=clusterId
    private readonly ConcurrentDictionary<string, List<ConsumerInfo>> consumers = new();
    //Key=exchange, cluster.result
    private readonly ConcurrentDictionary<string, RabbitConsumer> replyConsumers = new();
    private readonly ConcurrentDictionary<string, ResultWaiter> messageResults = new();
    private readonly ConcurrentDictionary<string, Func<string, Task<object>>> consumerHandlers = new();
    private readonly IServiceProvider serviceProvider;
    private readonly ILogger<MessageDrivenService> logger;

    internal ClusterRepository repository;
    private DateTime lastInitedTime = DateTime.MinValue;
    private DateTime lastUpdatedTime = DateTime.MinValue;
    public string HostName { get; set; }
    public string DbKey { get; set; }

    public MessageDrivenService(IServiceProvider serviceProvider)
    {
        this.serviceProvider = serviceProvider;
        this.logger = serviceProvider.GetService<ILogger<MessageDrivenService>>();

        this.task = Task.Factory.StartNew(async () =>
        {
            this.readyToStart.WaitOne();
            if (string.IsNullOrEmpty(this.DbKey))
            {
                this.logger.LogError("MessageDriven", "未设置dbKey,无法初始化MessageDrivenService对象");
                throw new Exception("未设置dbKey,无法初始化MessageDrivenService对象");
            }
            var logs = new List<ExecLog>();
            while (!this.cancellationSource.IsCancellationRequested)
            {
                try
                {
                    //每1分钟更新一次链接信息
                    if (DateTime.UtcNow - this.lastInitedTime > this.cycle)
                    {
                        await this.Initialize();
                        //确保Consumer是活的
                        this.EnsureAvailable();
                        this.lastInitedTime = DateTime.UtcNow;
                    }
                    if ((DateTime.UtcNow - this.lastUpdatedTime > TimeSpan.FromSeconds(10) && logs.Count > 0)
                        || logs.Count >= 100)
                    {
                        await this.repository.AddLogs(logs);
                        logs.Clear();
                        this.lastUpdatedTime = DateTime.UtcNow;
                    }
                    if (this.messageQueue.TryDequeue(out var message))
                    {
                        switch (message.Type)
                        {
                            case MessageType.OrgMessage:
                            case MessageType.TheaMessage:
                                var theaMessage = message.Body as TheaMessage;
                                if (!this.producers.TryGetValue(message.Exchange, out var producerInfo))
                                    throw new Exception($"未知的交换机{message.Exchange}，请先注册集群和生产者");

                                var messageBody = message.Type == MessageType.OrgMessage ? theaMessage.Message : theaMessage.ToJson();
                                if (message.ScheduleTimeUtc.HasValue)
                                    producerInfo.RabbitProducer.Schedule(message.Exchange, message.RoutingKey, message.ScheduleTimeUtc.Value, messageBody);
                                else
                                {
                                    if (producerInfo.IsNeedHashRoutingKey)
                                    {
                                        int routingKey = 0;
                                        if (producerInfo.ConsumerTotalCount > 1)
                                            routingKey = Math.Abs(HashCode.Combine(message.RoutingKey)) % producerInfo.ConsumerTotalCount;
                                        producerInfo.RabbitProducer.Publish(message.Exchange, routingKey.ToString(), messageBody);
                                    }
                                    else producerInfo.RabbitProducer.Publish(message.Exchange, message.RoutingKey, messageBody);
                                }
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
        if (string.IsNullOrEmpty(this.HostName))
            this.HostName = Dns.GetHostName();
        var dbFactory = this.serviceProvider.GetService<IOrmDbFactory>();
        this.repository = new ClusterRepository(dbFactory, this.DbKey);
        this.Register().Wait();
        this.readyToStart.Set();
    }
    public void Shutdown()
    {
        this.cancellationSource.Cancel();
        foreach (var producerInfo in this.producers.Values)
            producerInfo.RabbitProducer.Close();
        foreach (var consumerInfos in this.consumers.Values)
            consumerInfos.ForEach(f => f.RabbitConsumer.Shutdown());
        foreach (var consumer in this.replyConsumers.Values)
            consumer.Shutdown();
        if (this.task != null)
            this.task.Wait();
        this.cancellationSource.Dispose();
    }
    public void Publish<TMessage>(string exchange, string routingKey, TMessage message, bool isTheaMessage = true)
    {
        if (!this.producers.TryGetValue(exchange, out var producerInfo))
            throw new Exception($"未知的交换机{exchange}，请先注册集群和生产者");
        if (message == null)
            throw new ArgumentNullException(nameof(message));

        var theaMessage = new TheaMessage
        {
            MessageId = ObjectId.NewId(),
            Message = message.ToJson()
        };
        var messageType = isTheaMessage ? MessageType.TheaMessage : MessageType.OrgMessage;
        this.messageQueue.Enqueue(new Message { Type = messageType, Exchange = exchange, RoutingKey = routingKey, Body = theaMessage });
    }
    public Task PublishAsync<TMessage>(string exchange, string routingKey, TMessage message, bool isTheaMessage = true)
    {
        this.Publish(exchange, routingKey, message, isTheaMessage);
        return Task.CompletedTask;
    }
    public void Schedule<TMessage>(string exchange, string routingKey, TMessage message, DateTime enqueueTimeUtc, bool isTheaMessage = true)
    {
        if (enqueueTimeUtc < DateTime.UtcNow)
            throw new Exception($"只能选择未来时间");
        if (!exchange.EndsWith(".delay"))
            exchange += ".delay";
        if (!this.producers.TryGetValue(exchange, out _))
            throw new Exception($"未知的交换机{exchange}，请先注册集群和生产者");
        if (message == null)
            throw new ArgumentNullException(nameof(message));

        var theaMessage = new TheaMessage
        {
            MessageId = ObjectId.NewId(),
            Message = message.ToJson()
        };
        var messageType = isTheaMessage ? MessageType.TheaMessage : MessageType.OrgMessage;
        this.messageQueue.Enqueue(new Message
        {
            Type = messageType,
            Exchange = exchange,
            RoutingKey = routingKey,
            ScheduleTimeUtc = enqueueTimeUtc,
            Body = theaMessage
        });
    }
    public Task ScheduleAsync<TMessage>(string exchange, string routingKey, TMessage message, DateTime enqueueTimeUtc, bool isTheaMessage = true)
    {
        this.Schedule(exchange, routingKey, message, enqueueTimeUtc, isTheaMessage);
        return Task.CompletedTask;
    }
    public TResponse Request<TRequst, TResponse>(string exchange, string routingKey, TRequst message, bool isTheaMessage = true)
    {
        if (!this.producers.TryGetValue(exchange, out var producerInfo))
            throw new Exception($"未知的交换机{exchange}，请先注册集群和生产者");
        if (message == null)
            throw new ArgumentNullException(nameof(message));

        var theaMessage = new TheaMessage
        {
            MessageId = ObjectId.NewId(),
            ReplyExchange = exchange + ".result",
            ReplyRoutingKey = this.HostName,
            Status = MessageStatus.WaitForReply,
            Message = message.ToJson()
        };
        var resultWaiter = new ResultWaiter { ResponseType = typeof(TResponse), Waiter = new TaskCompletionSource<object>() };
        this.messageResults.TryAdd(theaMessage.MessageId, resultWaiter);
        var messageType = isTheaMessage ? MessageType.TheaMessage : MessageType.OrgMessage;
        this.messageQueue.Enqueue(new Message
        {
            Type = messageType,
            Exchange = exchange,
            RoutingKey = routingKey,
            Body = theaMessage
        });
        return (TResponse)resultWaiter.Waiter.Task.Result;
    }
    public async Task<TResponse> RequestAsync<TRequest, TResponse>(string exchange, string routingKey, TRequest message, bool isTheaMessage = true)
    {
        if (!this.producers.TryGetValue(exchange, out var producerInfo))
            throw new Exception($"未知的交换机{exchange}，请先注册集群和生产者");
        if (message == null)
            throw new ArgumentNullException(nameof(message));
        var theaMessage = new TheaMessage
        {
            MessageId = ObjectId.NewId(),
            ReplyExchange = exchange + ".result",
            Status = MessageStatus.WaitForReply,
            ReplyRoutingKey = this.HostName,
            Message = message.ToJson()
        };
        var resultWaiter = new ResultWaiter { ResponseType = typeof(TResponse), Waiter = new TaskCompletionSource<object>() };
        this.messageResults.TryAdd(theaMessage.MessageId, resultWaiter);
        var messageType = isTheaMessage ? MessageType.TheaMessage : MessageType.OrgMessage;
        this.messageQueue.Enqueue(new Message
        {
            Type = messageType,
            Exchange = exchange,
            RoutingKey = routingKey,
            Body = theaMessage
        });
        var result = await resultWaiter.Waiter.Task;
        return (TResponse)result;
    }

    public void AddProducer(string clusterId, bool isUseRpc = false)
    {
        this.producers.TryAdd(clusterId, new ProducerInfo { ClusterId = clusterId, Exchange = clusterId });
        if (!this.localClusterInfos.Exists(f => f.ClusterId == clusterId))
            this.localClusterInfos.Add(new ClusterInfo { ClusterId = clusterId });
        if (isUseRpc)
        {
            var exchange = clusterId + ".result";
            this.replyConsumers.TryAdd(exchange, new RabbitConsumer(this, this.serviceProvider));
        }
    }
    public void AddRpcReplyConsumer(string clusterId)
    {
        var exchange = clusterId + ".result";
        this.producers.TryAdd(exchange, new ProducerInfo { ClusterId = clusterId, Exchange = exchange });
        if (!this.localClusterInfos.Exists(f => f.ClusterId == clusterId))
            this.localClusterInfos.Add(new ClusterInfo { ClusterId = clusterId });
    }
    public void AddDelayProducer(string clusterId)
    {
        var exchange = clusterId + ".delay";
        this.producers.TryAdd(exchange, new ProducerInfo { ClusterId = clusterId, Exchange = exchange });
        if (!this.localClusterInfos.Exists(f => f.ClusterId == clusterId))
            this.localClusterInfos.Add(new ClusterInfo { ClusterId = clusterId });
    }
    public void AddStatefulConsumer(string clusterId, object target, MethodInfo methodInfo)
    {
        var parametersType = methodInfo.GetParameters().FirstOrDefault().ParameterType;
        var methodExecutor = ObjectMethodExecutor.Create(methodInfo, target.GetType().GetTypeInfo());
        Func<string, Task<object>> consumerHandler = async message =>
        {
            var parameters = TheaJsonSerializer.Deserialize(message, parametersType);
            object result;
            if (methodExecutor.IsMethodAsync)
                result = await methodExecutor.ExecuteAsync(target, parameters);
            else result = methodExecutor.Execute(target, parameters);
            return result;
        };
        //有状态队列，所有队列消费者都相同
        this.consumerHandlers.TryAdd(clusterId, consumerHandler);
        var queue = $"{clusterId}.queue0";
        var consumerInfo = new ConsumerInfo
        {
            ClusterId = clusterId,
            ConsumerId = $"{queue}.{this.HostName}.worker",
            Exchange = clusterId,
            BindType = "topic",
            RoutingKey = "0",
            Queue = queue,
            IsStateful = true,
            IsDelay = false,
            RabbitConsumer = new RabbitConsumer(this, this.serviceProvider, consumerHandler)
        };
        this.consumers.TryAdd(clusterId, new List<ConsumerInfo> { consumerInfo });
        var localClusterInfo = this.localClusterInfos.Find(f => f.ClusterId == clusterId);
        if (localClusterInfo == null)
            this.localClusterInfos.Add(new ClusterInfo { ClusterId = clusterId, IsStateful = true });
        else localClusterInfo.IsStateful = true;
    }
    public void AddSubscriber(string clusterId, string queue, object target, MethodInfo methodInfo, string routingKey = "#", bool isDelay = false)
    {
        var parametersType = methodInfo.GetParameters().FirstOrDefault().ParameterType;
        var methodExecutor = ObjectMethodExecutor.Create(methodInfo, target.GetType().GetTypeInfo());
        Func<string, Task<object>> consumerHandler = async message =>
        {
            var parameters = TheaJsonSerializer.Deserialize(message, parametersType);
            object result;
            if (methodExecutor.IsMethodAsync)
                result = await methodExecutor.ExecuteAsync(target, parameters);
            else result = methodExecutor.Execute(target, parameters);
            return result;
        };
        //无状态队列，不同的队列不同的消费者，根据不同的routingKey路由到不同的队列中
        this.consumerHandlers.TryAdd($"{clusterId}-{queue}", consumerHandler);
        var consumerInfo = new ConsumerInfo
        {
            ClusterId = clusterId,
            ConsumerId = $"{queue}.worker0",
            Exchange = isDelay ? clusterId + ".delay" : clusterId,
            BindType = isDelay ? "x-delayed-message" : "topic",
            //数据库可以更改
            RoutingKey = routingKey,
            Queue = queue,
            IsStateful = false,
            IsDelay = isDelay,
            RabbitConsumer = new RabbitConsumer(this, this.serviceProvider, consumerHandler)
        };
        if (!this.consumers.TryGetValue(clusterId, out var consumerInfos))
            this.consumers.TryAdd(clusterId, consumerInfos = new List<ConsumerInfo> { consumerInfo });
        if (!consumerInfos.Exists(f => f.Queue == queue))
            consumerInfos.Add(consumerInfo);
        if (!this.localClusterInfos.Exists(f => f.ClusterId == clusterId))
            this.localClusterInfos.Add(new ClusterInfo { ClusterId = clusterId });
    }
    public void Next(TheaMessage message, MessageStatus nextStatus)
    {
        ResultWaiter resultWaiter = null;
        switch (message.Status.Value)
        {
            case MessageStatus.WaitForReply:
                if (!this.producers.TryGetValue(message.ReplyExchange, out var producerInfo))
                {
                    lock (this)
                    {
                        var clusterId = message.ReplyExchange.Substring(0, message.ReplyExchange.Length - 8);
                        if (this.producers.TryGetValue(clusterId, out var clusterProducerInfo))
                        {
                            this.producers.TryAdd(message.ReplyExchange, producerInfo = new ProducerInfo
                            {
                                ClusterId = clusterId,
                                RabbitProducer = clusterProducerInfo.RabbitProducer
                            });
                        }
                        else throw new Exception($"未注册{clusterId} rpc应答消费者,调用AddRpcReplyConsumer({clusterId})方法注册rpc应答消费者");
                    }
                }
                message.Status = nextStatus;
                producerInfo.RabbitProducer.Publish(message.ReplyExchange, message.ReplyRoutingKey, message.ToJson());
                break;
            case MessageStatus.SetResult:
                if (this.messageResults.TryRemove(message.MessageId, out resultWaiter))
                {
                    var result = TheaJsonSerializer.Deserialize(message.Message, resultWaiter.ResponseType);
                    resultWaiter.Waiter?.TrySetResult(result);
                }
                break;
            case MessageStatus.SetException:
                if (this.messageResults.TryRemove(message.MessageId, out resultWaiter))
                    resultWaiter.Waiter?.TrySetException(new Exception(message.Message));
                break;
        }
    }
    public void AddLogs(ExecLog logInfo) => this.messageQueue.Enqueue(new Message
    {
        Type = MessageType.Logs,
        Body = logInfo
    });
    private void EnsureAvailable()
    {
        foreach (var workerConsumer in this.consumers.Values)
            workerConsumer.ForEach(f => f.RabbitConsumer.EnsureAvailable());
        foreach (var replyConsumer in this.replyConsumers.Values)
            replyConsumer.EnsureAvailable();
    }
    private async Task Register()
    {
        var localClusterIds = this.localClusterInfos.Select(f => f.ClusterId).ToList();
        (var dbClusters, var dbBindings) = await this.repository.GetClusterInfo(localClusterIds);
        var registerClusters = new List<Cluster>();
        var registerBindings = new List<Binding>();
        var ipAddress = this.GetIpAddress();

        foreach (var clusterId in localClusterIds)
        {
            var now = DateTime.UtcNow;
            var dbClusterInfo = dbClusters.Find(f => f.ClusterId == clusterId);
            if (dbClusterInfo == null)
            {
                registerClusters.Add(dbClusterInfo = new Cluster
                {
                    ClusterId = clusterId,
                    ClusterName = clusterId,
                    BindType = "topic",
                    IsEnabled = true,
                    CreatedAt = now,
                    CreatedBy = this.HostName,
                    UpdatedAt = now,
                    UpdatedBy = this.HostName
                });
            }
            this.clusters.TryAdd(clusterId, dbClusterInfo);
            if (!dbClusterInfo.IsEnabled) continue;

            if (!this.consumers.TryGetValue(clusterId, out var localConsumers)
                || localConsumers == null || localConsumers.Count == 0)
                continue;

            foreach (var localConsumer in localConsumers)
            {
                //判断队列交换机绑定是否存在
                if (!dbBindings.Exists(f => f.ClusterId == clusterId && f.Queue == localConsumer.Queue))
                {
                    registerBindings.Add(new Binding
                    {
                        BindingId = localConsumer.Queue,
                        ClusterId = clusterId,
                        BindType = localConsumer.IsDelay ? "x-delayed-message" : localConsumer.BindType,
                        BindingKey = localConsumer.RoutingKey,
                        Exchange = localConsumer.IsDelay ? clusterId + ".delay" : clusterId,
                        Queue = localConsumer.Queue,
                        PrefetchCount = 250,
                        IsSingleActiveConsumer = localConsumer.IsStateful,
                        IsReply = false,
                        IsDelay = localConsumer.IsDelay,
                        IsEnabled = true,
                        CreatedAt = now,
                        CreatedBy = this.HostName,
                        UpdatedAt = now,
                        UpdatedBy = this.HostName
                    });
                }
                dbClusterInfo.IsStateful = localConsumer.IsStateful;
                //只有有状态队列，才会有应答队列，订阅模式不会有应答队列
                var replyExchange = $"{clusterId}.result";
                var replyQueue = $"{clusterId}.{this.HostName}.result";
                if (this.replyConsumers.TryGetValue(replyExchange, out _)
                    && !dbBindings.Exists(f => f.ClusterId == clusterId && f.Queue == replyQueue))
                {
                    registerBindings.Add(new Binding
                    {
                        BindingId = replyQueue,
                        ClusterId = clusterId,
                        BindType = "direct",
                        BindingKey = this.HostName,
                        Exchange = replyExchange,
                        Queue = replyQueue,
                        PrefetchCount = 10,
                        IsSingleActiveConsumer = false,
                        IsReply = true,
                        IsDelay = false,
                        IsEnabled = true,
                        CreatedAt = now,
                        CreatedBy = this.HostName,
                        UpdatedAt = now,
                        UpdatedBy = this.HostName
                    });
                }
            }
        }
        if (registerClusters.Count > 0)
            await this.repository.Register(registerClusters);
        if (registerBindings.Count > 0)
            await this.repository.Register(registerBindings);
    }
    private async Task Initialize()
    {
        var localClusterIds = this.localClusterInfos.Select(f => f.ClusterId).ToList();
        (var dbClusters, var dbBindings) = await this.repository.GetClusterInfo(localClusterIds);
        var localClusterInfos = this.clusters.Values.ToList();
        var ipAddress = this.GetIpAddress();

        foreach (var localClusterInfo in localClusterInfos)
        {
            var dbClusterInfo = dbClusters.Find(f => f.ClusterId == localClusterInfo.ClusterId);
            //集群信息不存在或是无效，生产者和消费者都不建立
            if (dbClusterInfo == null || !dbClusterInfo.IsEnabled
                || string.IsNullOrEmpty(dbClusterInfo.Url)
                || string.IsNullOrEmpty(dbClusterInfo.User)
                || string.IsNullOrEmpty(dbClusterInfo.Password))
                continue;

            var clusterId = localClusterInfo.ClusterId;
            this.clusters[clusterId] = dbClusterInfo;
        }

        foreach (var producerInfo in this.producers.Values)
        {
            var dbClusterInfo = dbClusters.Find(f => f.ClusterId == producerInfo.ClusterId);
            if (dbClusterInfo == null || !dbClusterInfo.IsEnabled
            || string.IsNullOrEmpty(dbClusterInfo.Url)
            || string.IsNullOrEmpty(dbClusterInfo.User)
            || string.IsNullOrEmpty(dbClusterInfo.Password))
                continue;

            var totalCount = dbBindings.Count(f => f.ClusterId == producerInfo.ClusterId && f.Exchange == producerInfo.Exchange && !f.IsReply);
            producerInfo.ConsumerTotalCount = totalCount;
            producerInfo.IsNeedHashRoutingKey = dbClusterInfo.IsStateful;
            //相同Url/User/Password只建立一个生产者
            var hashKey = HashCode.Combine(dbClusterInfo.Url, dbClusterInfo.User, dbClusterInfo.Password);
            var rabbitProducer = this.rabbitProducers.GetOrAdd(hashKey, f =>
                new RabbitProducer().Create($"{this.HostName}.producer", dbClusterInfo));
            if (producerInfo.RabbitProducer == null)
                producerInfo.RabbitProducer = rabbitProducer;
        }

        foreach (var localClusterInfo in localClusterInfos)
        {
            var clusterId = localClusterInfo.ClusterId;
            var dbClusterInfo = dbClusters.Find(f => f.ClusterId == clusterId);
            if (dbClusterInfo == null || !dbClusterInfo.IsEnabled
            || string.IsNullOrEmpty(dbClusterInfo.Url)
            || string.IsNullOrEmpty(dbClusterInfo.User)
            || string.IsNullOrEmpty(dbClusterInfo.Password))
                continue;

            var clusterBindings = dbBindings.FindAll(f => f.ClusterId == clusterId);
            //没有消费者
            if (clusterBindings == null || clusterBindings.Count == 0)
                continue;

            if (!this.consumers.TryGetValue(clusterId, out var localConsumerInfos))
                continue;

            //没有可用的绑定信息，跳过
            var requiredBindings = clusterBindings.FindAll(f => !f.IsReply && f.IsEnabled);
            if (requiredBindings.Count == 0)
                continue;

            if (requiredBindings.Count > 1)
                requiredBindings.Sort((x, y) => x.BindingKey.CompareTo(y.BindingKey));

            //订阅和有状态队列
            for (int i = 0; i < requiredBindings.Count; i++)
            {
                var dbBindingInfo = requiredBindings[i];
                var localConsumerInfo = localConsumerInfos.Find(f => f.Queue == dbBindingInfo.Queue);
                if (localConsumerInfo == null)
                {
                    localConsumerInfo = new ConsumerInfo
                    {
                        ConsumerId = $"{dbBindingInfo.Queue}.{this.HostName}.worker",
                        ClusterId = clusterId,
                        RoutingKey = dbBindingInfo.BindingKey,
                        Queue = dbBindingInfo.Queue
                    };
                    var consumerHandlerKey = dbClusterInfo.IsStateful ? clusterId : $"{clusterId}-{dbBindingInfo.Queue}";
                    localConsumerInfo.RabbitConsumer = new RabbitConsumer(this, this.serviceProvider, this.consumerHandlers[consumerHandlerKey]);
                    localConsumerInfos.Add(localConsumerInfo);
                }
                localConsumerInfo.RabbitConsumer.Build(localConsumerInfo.ConsumerId, dbClusterInfo, dbBindingInfo);
            }
            //应答队列
            var replyExchange = $"{clusterId}.result";
            var replyQueue = $"{clusterId}.{this.HostName}.result";
            if (this.replyConsumers.TryGetValue(replyExchange, out var replyRabbitConsumer))
            {
                var replyBinding = clusterBindings.Find(f => f.Queue == replyQueue && f.IsReply && f.IsEnabled);
                if (replyBinding != null) replyRabbitConsumer.Build(replyQueue, dbClusterInfo, replyBinding);
            }

            //多余的本地消费者标记为删除，删除队列，一定要从后面往前删除，以免丢失消息
            var removeConsumers = localConsumerInfos.FindAll(f => !requiredBindings.Exists(t => f.Queue == t.Queue));
            if (removeConsumers.Count > 0)
            {
                foreach (var removeConsumer in removeConsumers)
                {
                    this.deferredRemovedConsumers.Add(new DeferredRemovedConsumer
                    {
                        RabbitConsumer = removeConsumer.RabbitConsumer,
                        RemovedAt = DateTime.Now
                    });
                    localConsumerInfos.Remove(removeConsumer);
                }
            }
        }
        //超过两个初始化周期，删除无用消费者
        if (this.deferredRemovedConsumers.Count > 0)
        {
            foreach (var deferredRemovedConsumer in this.deferredRemovedConsumers)
            {
                if (DateTime.Now - deferredRemovedConsumer.RemovedAt > this.cycle * 2)
                {
                    deferredRemovedConsumer.RabbitConsumer.Shutdown();
                    //删除队列
                    deferredRemovedConsumer.RabbitConsumer.RemoveQueue();
                }
            }
        }
    }
    private string GetIpAddress()
    {
        foreach (var item in NetworkInterface.GetAllNetworkInterfaces())
        {
            if ((item.NetworkInterfaceType == NetworkInterfaceType.Ethernet
                || item.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)
                && item.OperationalStatus == OperationalStatus.Up)
            {
                var properties = item.GetIPProperties();
                if (properties.GatewayAddresses.Count > 0)
                {
                    foreach (UnicastIPAddressInformation ip in properties.UnicastAddresses)
                    {
                        if (ip.Address.AddressFamily == AddressFamily.InterNetwork)
                            return ip.Address.ToString();
                    }
                }
            }
        }
        return string.Empty;
    }

    class ClusterInfo
    {
        public string ClusterId { get; set; }
        public bool IsStateful { get; set; }
    }
    struct DeferredRemovedConsumer
    {
        public RabbitConsumer RabbitConsumer { get; set; }
        public DateTime RemovedAt { get; set; }
    }
}
