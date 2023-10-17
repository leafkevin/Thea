using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO.MemoryMappedFiles;
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
    private readonly List<string> localClusterIds = new();
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
    private readonly ConcurrentDictionary<string, ConsumerType> consumerTypes = new();
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
                    }
                    if (this.messageQueue.TryDequeue(out var message))
                    {
                        switch (message.Type)
                        {
                            case MessageType.OrgMessage:
                            case MessageType.TheaMessage:
                                var theaMessage = message.Body as TheaMessage;
                                if (!this.producers.TryGetValue(theaMessage.Exchange, out var producerInfo))
                                    throw new Exception($"未知的交换机{theaMessage.Exchange}，请先注册集群和生产者");

                                int routingKey = 0;
                                if (producerInfo.ConsumerTotalCount > 1)
                                    routingKey = Math.Abs(HashCode.Combine(theaMessage.RoutingKey)) % producerInfo.ConsumerTotalCount;
                                var messageBody = message.Type == MessageType.OrgMessage ? theaMessage.Message : theaMessage.ToJson();

                                //延时消息
                                if (theaMessage.ScheduleTimeUtc.HasValue)
                                    producerInfo.RabbitProducer.Schedule(theaMessage.Exchange, routingKey.ToString(), theaMessage.ScheduleTimeUtc.Value, messageBody);
                                else producerInfo.RabbitProducer.Publish(theaMessage.Exchange, routingKey.ToString(), messageBody);
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
                    this.logger.LogTagError("MessageDriven", ex, "MessageDriven:消费者守护宿主线程执行异常");
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
            HostName = this.HostName,
            ClusterId = producerInfo.ClusterId,
            Exchange = exchange,
            RoutingKey = routingKey,
            Message = message.ToJson(),
            Status = MessageStatus.None
        };
        var messageType = isTheaMessage ? MessageType.TheaMessage : MessageType.OrgMessage;
        this.messageQueue.Enqueue(new Message { Type = messageType, Body = theaMessage });
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
        if (!this.producers.TryGetValue(exchange, out var producerInfo))
            throw new Exception($"未知的交换机{exchange}，请先注册集群和生产者");
        if (message == null)
            throw new ArgumentNullException(nameof(message));

        var theaMessage = new TheaMessage
        {
            MessageId = ObjectId.NewId(),
            HostName = this.HostName,
            ClusterId = producerInfo.ClusterId,
            Exchange = exchange,
            RoutingKey = routingKey,
            Message = message.ToJson(),
            ScheduleTimeUtc = enqueueTimeUtc,
            Status = MessageStatus.None
        };
        var messageType = isTheaMessage ? MessageType.TheaMessage : MessageType.OrgMessage;
        this.messageQueue.Enqueue(new Message { Type = messageType, Body = theaMessage });
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
            HostName = this.HostName,
            ClusterId = producerInfo.ClusterId,
            Exchange = exchange,
            ReplyExchange = exchange + ".result",
            RoutingKey = routingKey,
            Message = message.ToJson(),
            Status = MessageStatus.WaitForReply
        };
        var resultWaiter = new ResultWaiter { ResponseType = typeof(TResponse), Waiter = new TaskCompletionSource<object>() };
        this.messageResults.TryAdd(theaMessage.MessageId, resultWaiter);
        var messageType = isTheaMessage ? MessageType.TheaMessage : MessageType.OrgMessage;
        this.messageQueue.Enqueue(new Message { Type = messageType, Body = theaMessage });
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
            HostName = this.HostName,
            ClusterId = producerInfo.ClusterId,
            Exchange = exchange,
            ReplyExchange = exchange + ".result",
            RoutingKey = routingKey,
            Message = message.ToJson(),
            Status = MessageStatus.WaitForReply
        };
        var resultWaiter = new ResultWaiter { ResponseType = typeof(TResponse), Waiter = new TaskCompletionSource<object>() };
        this.messageResults.TryAdd(theaMessage.MessageId, resultWaiter);
        var messageType = isTheaMessage ? MessageType.TheaMessage : MessageType.OrgMessage;
        this.messageQueue.Enqueue(new Message { Type = messageType, Body = theaMessage });
        var result = await resultWaiter.Waiter.Task;
        return (TResponse)result;
    }

    public void AddProducer(string clusterId, bool isUseRpc)
    {
        this.producers.TryAdd(clusterId, new ProducerInfo { ClusterId = clusterId });
        if (isUseRpc)
        {
            var exchange = clusterId + ".result";
            this.replyConsumers.TryAdd(exchange, new RabbitConsumer(this, this.serviceProvider));
        }
        if (!this.localClusterIds.Contains(clusterId))
            this.localClusterIds.Add(clusterId);
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
        this.consumerHandlers.TryAdd(clusterId, consumerHandler);
        var consumerInfo = new ConsumerInfo
        {
            ClusterId = clusterId,
            ConsumerId = $"{clusterId}.{this.HostName}.worker0",
            RoutingKey = "0",
            Queue = $"{clusterId}.queue0",
            IsStateful = true,
            RabbitConsumer = new RabbitConsumer(this, this.serviceProvider, consumerHandler)
        };
        this.consumers.TryAdd(clusterId, new List<ConsumerInfo> { consumerInfo });
        this.consumerTypes.TryAdd(clusterId, ConsumerType.StatefulConsumer);
        if (!this.localClusterIds.Contains(clusterId))
            this.localClusterIds.Add(clusterId);
    }
    public void AddSubscriber(string clusterId, string queue, object target, MethodInfo methodInfo)
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
        this.consumerHandlers.TryAdd(clusterId, consumerHandler);
        var consumerInfo = new ConsumerInfo
        {
            ClusterId = clusterId,
            ConsumerId = $"{queue}.worker0",
            RoutingKey = "#",
            Queue = queue,
            IsStateful = false,
            RabbitConsumer = new RabbitConsumer(this, this.serviceProvider, consumerHandler)
        };
        if (!this.consumers.TryGetValue(clusterId, out var consumerInfos))
            this.consumers.TryAdd(clusterId, consumerInfos = new List<ConsumerInfo> { consumerInfo });
        if (!consumerInfos.Exists(f => f.Queue == queue))
            consumerInfos.Add(consumerInfo);
        this.consumerTypes.TryAdd(clusterId, ConsumerType.Subscriber);
        if (!this.localClusterIds.Contains(clusterId))
            this.localClusterIds.Add(clusterId);
    }
    public void Next(TheaMessage message, Exception exception)
    {
        ResultWaiter resultWaiter = null;
        switch (message.Status)
        {
            case MessageStatus.WaitForReply:
                if (!this.producers.TryGetValue(message.ReplyExchange, out var producer))
                    this.producers.TryAdd(message.ReplyExchange, new ProducerInfo { ClusterId = message.ClusterId, RabbitProducer = new RabbitProducer() });

                producer.RabbitProducer.Publish(message.ReplyExchange, message.HostName, message.ToJson());
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
                    resultWaiter.Waiter?.TrySetException(exception);
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
        (var dbClusters, var dbBindings) = await this.repository.GetClusterInfo(this.localClusterIds);
        var registerClusters = new List<Cluster>();
        var registerBindings = new List<Binding>();
        var ipAddress = this.GetIpAddress();

        foreach (var clusterId in this.localClusterIds)
        {
            var now = DateTime.UtcNow;
            var dbClusterInfo = dbClusters.Find(f => f.ClusterId == clusterId);
            this.consumerTypes.TryGetValue(clusterId, out var consumerType);
            if (dbClusterInfo == null)
            {
                registerClusters.Add(dbClusterInfo = new Cluster
                {
                    ClusterId = clusterId,
                    ClusterName = clusterId,
                    BindType = consumerType == ConsumerType.StatefulConsumer ? "direct" : "topic",
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
                var myClusterBindings = dbBindings.FindAll(f => f.ClusterId == clusterId && f.Queue == localConsumer.Queue);
                //判断队列交换机绑定是否存在
                if (myClusterBindings == null || myClusterBindings.Count == 0)
                {
                    registerBindings.Add(new Binding
                    {
                        BindingId = localConsumer.Queue,
                        ClusterId = clusterId,
                        BindType = consumerType == ConsumerType.StatefulConsumer ? "direct" : "topic",
                        BindingKey = localConsumer.RoutingKey,
                        Exchange = clusterId,
                        Queue = localConsumer.Queue,
                        PrefetchCount = 250,
                        IsSingleActiveConsumer = localConsumer.IsStateful,
                        IsReply = false,
                        IsEnabled = true,
                        CreatedAt = now,
                        CreatedBy = this.HostName,
                        UpdatedAt = now,
                        UpdatedBy = this.HostName
                    });
                }
                //只有有状态队列，才会有应答队列，订阅模式不会有应答队列
                var replyQueue = $"{clusterId}.{this.HostName}.result";
                if (this.replyConsumers.TryGetValue(replyQueue, out _)
                    && !dbBindings.Exists(f => f.ClusterId == clusterId && f.Queue == replyQueue))
                {
                    registerBindings.Add(new Binding
                    {
                        BindingId = replyQueue,
                        ClusterId = clusterId,
                        BindType = "direct",
                        BindingKey = this.HostName,
                        Exchange = $"{clusterId}.result",
                        Queue = replyQueue,
                        HostName = this.HostName,
                        PrefetchCount = 10,
                        IsSingleActiveConsumer = false,
                        IsReply = true,
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
        (var dbClusters, var dbBindings) = await this.repository.GetClusterInfo(this.localClusterIds);
        var localClusterInfos = this.clusters.Values.ToList();
        var ipAddress = this.GetIpAddress();

        foreach (var localClusterInfo in localClusterInfos)
        {
            var dbClusterInfo = dbClusters.Find(f => f.ClusterId == localClusterInfo.ClusterId);
            var clusterBindings = dbBindings.FindAll(f => f.ClusterId == localClusterInfo.ClusterId);

            //集群信息不存在或是无效，生产者和消费者都不建立
            if (dbClusterInfo == null || !dbClusterInfo.IsEnabled || string.IsNullOrEmpty(dbClusterInfo.Url))
                continue;

            var clusterId = localClusterInfo.ClusterId;
            this.clusters[clusterId] = dbClusterInfo;

            bool isNeedCreateExchange = true;
            if (this.producers.TryGetValue(clusterId, out var producerInfo))
            {
                var totalCount = dbBindings.Count(f => f.ClusterId == clusterId && !f.IsReply);
                producerInfo.ConsumerTotalCount = totalCount;
                //相同Url/User/Password只建立一个生产者
                var hashKey = HashCode.Combine(dbClusterInfo.Url, dbClusterInfo.User, dbClusterInfo.Password);
                var rabbitProducer = this.rabbitProducers.GetOrAdd(hashKey, f =>
                    new RabbitProducer().Create($"{this.HostName}.producer", dbClusterInfo));
                if (producerInfo.RabbitProducer == null)
                    producerInfo.RabbitProducer = rabbitProducer;
                //创建Exchange
                rabbitProducer.CreateExchange(dbClusterInfo, this.HostName);
                isNeedCreateExchange = false;
            }

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
                        ConsumerId = $"{clusterId}.{this.HostName}.worker{i}",
                        ClusterId = clusterId,
                        RoutingKey = dbBindingInfo.BindingKey,
                        Queue = dbBindingInfo.Queue
                    };
                    localConsumerInfo.RabbitConsumer = new RabbitConsumer(this, this.serviceProvider, this.consumerHandlers[clusterId]);
                    localConsumerInfos.Add(localConsumerInfo);
                }
                localConsumerInfo.RabbitConsumer.Build(localConsumerInfo.ConsumerId, dbClusterInfo, dbBindingInfo, isNeedCreateExchange);
                isNeedCreateExchange = false;
            }
            //应答队列
            var replyQueue = $"{clusterId}.{this.HostName}.result";
            if (this.replyConsumers.TryGetValue(replyQueue, out var replyRabbitConsumer))
            {
                var replyBinding = clusterBindings.Find(f => f.HostName == this.HostName && f.IsReply && f.IsEnabled);
                if (replyBinding != null) replyRabbitConsumer.Build(replyQueue, dbClusterInfo, replyBinding, isNeedCreateExchange);
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
            if (item.NetworkInterfaceType == NetworkInterfaceType.Ethernet && item.OperationalStatus == OperationalStatus.Up)
            {
                var properties = item.GetIPProperties();
                if (properties.GatewayAddresses.Count > 0)
                {
                    foreach (UnicastIPAddressInformation ip in properties.UnicastAddresses)
                    {
                        if (ip.Address.AddressFamily == AddressFamily.InterNetwork)
                        {
                            return ip.Address.ToString();
                        }
                    }
                }
            }
        }
        return string.Empty;
    }
    enum ConsumerType
    {
        Subscriber,
        StatefulConsumer
    }
    struct DeferredRemovedConsumer
    {
        public RabbitConsumer RabbitConsumer { get; set; }
        public DateTime RemovedAt { get; set; }
    }
}