using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Thea.Logging;

namespace Thea.MessageDriven;

class MessageDrivenService : IMessageDriven
{
    private readonly Task task;
    private readonly TimeSpan heartbeatCycle;
    private readonly CancellationTokenSource cancellationSource = new CancellationTokenSource();
    private readonly EventWaitHandle readyToStart = new EventWaitHandle(false, EventResetMode.AutoReset);
    private readonly ConcurrentDictionary<string, List<RabbitConsumer>> consumers = new();
    private readonly ConcurrentDictionary<string, WaitForStartMessage> waitingStartConsumers = new();
    private readonly ConcurrentDictionary<string, List<RabbitConsumer>> waitingShutdownConsumers = new();
    private readonly ConcurrentDictionary<string, DateTime> nodeHeartbeats = new();
    private readonly ConcurrentQueue<Message> messageQueue = new();

    private bool hasConsumer = false;
    private List<Cluster> localClusters = new();
    private List<Cluster> lastClusters = null;
    private RabbitProducer rabbitProducer;
    private RabbitConsumer heartbeatRabbitConsumer;
    private readonly ConcurrentDictionary<string, (Type, Delegate)> consumerHandlers = new();
    private readonly IServiceProvider serviceProvider;
    private readonly ILogger<MessageDrivenService> logger;

    private IMessageDrivenRepository repository;
    private DateTime lastInitedTime = DateTime.MinValue;
    private DateTime lastUpdatedTime = DateTime.MinValue;
    private DateTime lastLoggedTime = DateTime.MinValue;
    private int sacCount = 3;
    public string AppId { get; private set; }
    public string NodeId { get; private set; }

    public MessageDrivenService(IServiceProvider serviceProvider)
    {
        this.serviceProvider = serviceProvider;
        this.logger = serviceProvider.GetService<ILogger<MessageDrivenService>>();
        var configuration = serviceProvider.GetService<IConfiguration>();
        this.heartbeatCycle = TimeSpan.FromSeconds(configuration.GetValue("MessageDriven:Heartbeat", 10));
        this.AppId = configuration.GetValue<string>("AppId");
        if (string.IsNullOrEmpty(this.AppId))
        {
            this.logger.LogTagError("MessageDriven", "未设置AppId，无法初始化MessageDrivenService对象");
            throw new Exception("未设置AppId，无法初始化MessageDrivenService对象");
        }
        this.sacCount = configuration.GetValue("MessageDriven:SacCount", 3);
        this.NodeId = ObjectId.NewId();

        this.task = Task.Factory.StartNew(async () =>
        {
            this.readyToStart.WaitOne();
            var logs = new List<ExecLog>();
            while (!this.cancellationSource.IsCancellationRequested)
            {
                try
                {
                    //每10秒发送一次心跳，根据配置更新本地集群配置信息localClusters，配置中心或是数据库会有更改，比如：临时禁用某个集群
                    if (DateTime.Now - this.lastInitedTime >= this.heartbeatCycle)
                    {
                        await this.Initialize();
                        if (this.hasConsumer)
                            this.SendHeartbeat();
                        this.lastInitedTime = DateTime.Now;
                    }
                    //经过1.5个心跳后，根据前面获取的最新配置信息和最新服务器信息，启动本AppId下的所有消费者                    
                    if (DateTime.Now - this.lastUpdatedTime > this.heartbeatCycle * 1.5)
                    {
                        if (this.hasConsumer) this.StartConsumers();
                        this.lastUpdatedTime = DateTime.Now;
                    }
                    if ((DateTime.Now - this.lastLoggedTime > TimeSpan.FromSeconds(10) && logs.Count > 0)
                        || logs.Count >= 100)
                    {
                        await this.repository.WriteLogs(logs);
                        logs.Clear();
                        this.lastLoggedTime = DateTime.Now;
                    }
                    if (this.messageQueue.TryDequeue(out var message))
                    {
                        string queueName = null;
                        TaskCompletionSource<bool> waiter = null;
                        switch (message.Type)
                        {
                            case MessageType.Message:
                                this.rabbitProducer ??= new RabbitProducer(this, this.serviceProvider);
                                if (message.ScheduleTimeUtc.HasValue)
                                    this.rabbitProducer.Schedule(message.Exchange, message.RoutingKey, message.ScheduleTimeUtc.Value, message.ToJson());
                                else
                                {
                                    var cluster = this.localClusters.Find(f => f.ClusterId == message.Exchange);
                                    if (cluster == null)
                                        throw new Exception($"未知的交换机{message.Exchange}，请先注册集群和生产者");

                                    if (cluster.IsStateful)
                                    {
                                        int routingKey = 0;
                                        if (cluster.WorkloadTotal > 1)
                                        {
                                            var hashKey = HashCode.Combine(message.RoutingKey);
                                            routingKey = hashKey % cluster.WorkloadTotal;
                                            if (routingKey < 0) routingKey += cluster.WorkloadTotal;
                                            message.RoutingKey = routingKey.ToString();
                                        }
                                        this.rabbitProducer.Publish(message.Exchange, message.RoutingKey, message.ToJson());
                                    }
                                    else this.rabbitProducer.Publish(message.Exchange, message.RoutingKey, message.ToJson());
                                }
                                break;
                            case MessageType.Heartbeat:
                                //统一处理心跳，可防止并发
                                (var nodeId, waiter) = ((string, TaskCompletionSource<bool>))message.Body;
                                this.nodeHeartbeats.AddOrUpdate(nodeId, DateTime.Now, (k, o) => DateTime.Now);
                                waiter.TrySetResult(true);
                                break;
                            case MessageType.WaitForStart:
                                (queueName, waiter) = ((string, TaskCompletionSource<bool>))message.Body;
                                if (this.waitingStartConsumers.TryGetValue(message.Exchange, out var waitingMessage))
                                {
                                    if (!waitingMessage.QueueNames.Contains(queueName))
                                        waitingMessage.QueueNames.Add(queueName);
                                    if (waitingMessage.QueueNames.Count >= waitingMessage.WaitTotal)
                                        waiter.TrySetResult(true);
                                }
                                else waiter.TrySetResult(true);
                                break;
                            case MessageType.WaitForShutdown:
                                if (this.waitingShutdownConsumers.TryGetValue(message.Exchange, out var rabbitConsumers))
                                {
                                    rabbitConsumers.ForEach(f => f.Shutdown());
                                    waiter.TrySetResult(true);
                                }
                                else waiter.TrySetResult(true);
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
        this.nodeHeartbeats.TryAdd(this.NodeId, DateTime.Now);
        this.readyToStart.Set();
    }
    public void Shutdown()
    {
        this.cancellationSource.Cancel();
        this.rabbitProducer.Shutdown();
        foreach (var rabbitConsumers in this.consumers.Values)
            rabbitConsumers.ForEach(f => f.Shutdown());
        this.consumers.Clear();

        if (this.task != null)
            this.task.Wait();
        this.cancellationSource.Dispose();
    }
    public void Publish<TMessage>(string exchange, string routingKey, TMessage message, bool isTheaMessage = true)
    {
        if (!this.localClusters.Exists(f => f.ClusterId == exchange))
            throw new Exception($"未知的交换机{exchange}，请先注册集群:{exchange}，使用UseProducer或是UseStatefulConsumer、UseSubscriber方法");
        if (message == null)
            throw new ArgumentNullException(nameof(message));

        this.messageQueue.Enqueue(new Message
        {
            MessageId = ObjectId.NewId(),
            AppId = this.AppId,
            Exchange = exchange,
            RoutingKey = routingKey,
            Body = message.ToJson()
        });
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
        if (!this.localClusters.Exists(f => f.ClusterId == exchange))
            throw new Exception($"未知的交换机{exchange}，请先注册集群:{exchange}，使用UseProducer或是UseStatefulConsumer、UseSubscriber方法");

        if (message == null)
            throw new ArgumentNullException(nameof(message));

        this.messageQueue.Enqueue(new Message
        {
            MessageId = ObjectId.NewId(),
            AppId = this.AppId,
            Exchange = exchange,
            RoutingKey = routingKey,
            ScheduleTimeUtc = enqueueTimeUtc,
            Body = message.ToJson()
        });
    }
    public Task ScheduleAsync<TMessage>(string exchange, string routingKey, TMessage message, DateTime enqueueTimeUtc, bool isTheaMessage = true)
    {
        this.Schedule(exchange, routingKey, message, enqueueTimeUtc, isTheaMessage);
        return Task.CompletedTask;
    }
    public void UseProducer(params string[] clusterIds)
    {
        foreach (var clusterId in clusterIds)
        {
            if (this.localClusters.Exists(f => f.ClusterId == clusterId))
                continue;
            this.localClusters.Add(new Cluster
            {
                ClusterId = clusterId,
                ClusterName = clusterId,
                Exchange = clusterId,
                IsEnabled = true
            });
        }
    }
    public void UseStatefulConsumer<TParameters>(string clusterId, Func<TParameters, Task> consumer)
    {
        this.hasConsumer = true;
        var parametersType = typeof(TParameters);
        Func<object, Task> consumerHandler = message => (Task)consumer.DynamicInvoke(message);
        this.consumerHandlers.TryAdd(clusterId, (parametersType, consumerHandler));
        if (!this.localClusters.Exists(f => f.ClusterId == clusterId))
        {
            this.localClusters.Add(new Cluster
            {
                ClusterId = clusterId,
                ClusterName = clusterId,
                Exchange = clusterId,
                IsStateful = true,
                BindType = "topic",
                IsSac = true,
                IsDelay = false,
                Queue = $"{clusterId}.queue",
                PrefetchCount = 250,
                WorkloadTotal = 2,
                IsEnabled = true,
                IsLogEnabled = false,
                UpdatedAt = DateTime.Now
            });
        }
    }
    public void UseStatefulConsumer(string clusterId, object target, MethodInfo methodInfo)
    {
        this.hasConsumer = true;
        var parametersType = methodInfo.GetParameters().FirstOrDefault().ParameterType;
        var methodExecutor = ObjectMethodExecutor.Create(methodInfo, target.GetType().GetTypeInfo());
        Func<object, Task> consumerHandler = methodExecutor.IsMethodAsync ? async message =>
            await methodExecutor.ExecuteAsync(target, [message]) : message =>
            {
                methodExecutor.Execute(target, [message]);
                return Task.CompletedTask;
            };
        this.consumerHandlers.TryAdd(clusterId, (parametersType, consumerHandler));
        if (!this.localClusters.Exists(f => f.ClusterId == clusterId))
        {
            this.localClusters.Add(new Cluster
            {
                ClusterId = clusterId,
                ClusterName = clusterId,
                Exchange = clusterId,
                IsStateful = true,
                BindType = "topic",
                IsSac = true,
                IsDelay = false,
                Queue = $"{clusterId}.queue",
                PrefetchCount = 250,
                WorkloadTotal = 2,
                IsEnabled = true,
                IsLogEnabled = false,
                UpdatedAt = DateTime.Now
            });
        }
    }
    public void UseSubscriber<TParameters>(string clusterId, string queue, Func<TParameters, Task> consumer, string routingKey = "#", bool isDelay = false)
    {
        this.hasConsumer = true;
        //无状态队列，不同的队列不同的消费者，根据不同的routingKey路由到不同的队列中，订阅者是默认是# topic
        Func<object, Task> consumerHandler = message => (Task)consumer.DynamicInvoke(message);
        this.consumerHandlers.TryAdd($"{clusterId}-{queue}", (typeof(TParameters), consumerHandler));
        if (!this.localClusters.Exists(f => f.ClusterId == clusterId))
        {
            this.localClusters.Add(new Cluster
            {
                ClusterId = clusterId,
                ClusterName = clusterId,
                Exchange = clusterId,
                IsStateful = false,
                BindType = isDelay ? "x-delayed-message" : "topic",
                BindingKey = routingKey,
                IsSac = false,
                IsDelay = isDelay,
                Queue = queue,
                PrefetchCount = 5,
                WorkloadTotal = 2,
                IsEnabled = true,
                IsLogEnabled = false,
                UpdatedAt = DateTime.Now
            });
        }
    }
    public void UseSubscriber(string clusterId, string queue, object target, MethodInfo methodInfo, string routingKey = "#", bool isDelay = false)
    {
        this.hasConsumer = true;
        var parametersType = methodInfo.GetParameters().FirstOrDefault().ParameterType;
        var methodExecutor = ObjectMethodExecutor.Create(methodInfo, target.GetType().GetTypeInfo());
        Func<object, Task> consumerHandler = methodExecutor.IsMethodAsync ? async message =>
            await methodExecutor.ExecuteAsync(target, [message]) : message =>
            {
                methodExecutor.Execute(target, [message]);
                return Task.CompletedTask;
            };
        //无状态队列，不同的队列不同的消费者，根据不同的routingKey路由到不同的队列中
        this.consumerHandlers.TryAdd($"{clusterId}-{queue}", (parametersType, consumerHandler));
        if (!this.localClusters.Exists(f => f.ClusterId == clusterId))
        {
            this.localClusters.Add(new Cluster
            {
                ClusterId = clusterId,
                ClusterName = clusterId,
                Exchange = clusterId,
                IsStateful = false,
                BindType = isDelay ? "x-delayed-message" : "topic",
                BindingKey = routingKey,
                IsSac = false,
                IsDelay = isDelay,
                Queue = queue,
                PrefetchCount = 5,
                WorkloadTotal = 2,
                IsEnabled = true,
                IsLogEnabled = false,
                UpdatedAt = DateTime.Now
            });
        }
    }

    internal void UseRepository(IMessageDrivenRepository repository) => this.repository = repository;
    internal void AddLogs(ExecLog logInfo) => this.messageQueue.Enqueue(new Message
    {
        MessageId = ObjectId.NewId(),
        Type = MessageType.Logs,
        Body = logInfo
    });
    internal void ProcessMessage(Message message) => this.messageQueue.Enqueue(message);
    private async Task Register()
    {
        var clusterIds = this.localClusters.Select(f => f.ClusterId).ToList();
        //捞取数据库或是配置中心的集群信息        
        var dbClusters = await this.repository.GetClusters(clusterIds);
        var registerClusters = new List<Cluster>();
        foreach (var clusterId in clusterIds)
        {
            if (dbClusters.Exists(f => clusterIds.Contains(f.ClusterId)))
                continue;
            var cluster = this.localClusters.Find(f => f.ClusterId == clusterId);
            registerClusters.Add(cluster);
        }
        //代码中有配置集群信息，但是数据库或是配置中心没有，需要注册，如果需要删除集群配置，需要在代码中要删除
        if (registerClusters.Count > 0)
            await this.repository.Register(registerClusters);

        this.rabbitProducer = new RabbitProducer(this, this.serviceProvider);

        //没有消费者，什么都不做，也不创建
        if (!this.hasConsumer) return;
        this.rabbitProducer.CreateExchange("heartbeat", "fanout");
        var queueName = $"heartbeat.{this.NodeId}";
        this.heartbeatRabbitConsumer = new RabbitConsumer("heartbeat", queueName, this, this.serviceProvider, true, typeof(string));
        this.heartbeatRabbitConsumer.Start();

        //创建信箱和队列
        foreach (var cluster in this.localClusters)
        {
            if (!cluster.IsEnabled)
                continue;

            var exchange = cluster.ClusterId;
            rabbitProducer.CreateExchange(exchange, cluster.BindType, cluster.IsDelay);
            Console.WriteLine($"ClusterId: {exchange}, BindType: {cluster.BindType}");
            if (cluster.IsStateful)
            {
                for (int i = 0; i < cluster.WorkloadTotal; i++)
                {
                    queueName = $"{cluster.Queue}.{i}";
                    this.rabbitProducer.CreateQueue(queueName, cluster.IsSac, false);
                    this.rabbitProducer.BindQueue(exchange, queueName, i.ToString());
                    Console.WriteLine($"ClusterId: {exchange}, BindingKey: {i}, Queue: {queueName}");
                }
            }
            else
            {
                this.rabbitProducer.CreateQueue(cluster.Queue, false, false);
                this.rabbitProducer.BindQueue(exchange, cluster.Queue, "#");
            }
        }
        this.SendHeartbeat();
    }
    private async Task Initialize()
    {
        var clusterIds = this.localClusters.Select(f => f.ClusterId).ToList();
        var isFirst = this.lastClusters == null;
        //获取数据库或是配置中心的集群信息
        var dbClusters = await this.repository.GetClusters(clusterIds);
        this.lastClusters = this.localClusters;
        //对比本地集群和数据库集群，并更新本地集群配置信息
        var newClusters = new List<Cluster>();
        foreach (var cluster in this.lastClusters)
        {
            var dbCluster = dbClusters.Find(f => f.ClusterId == cluster.ClusterId);
            //数据库或是配置中心的配置信息已删除或是禁用不创建消费者
            if (dbCluster == null || !dbCluster.IsEnabled)
                continue;
            newClusters.Add(dbCluster);
        }
        this.localClusters = newClusters;
    }
    private void SendHeartbeat()
    {
        var message = new Message
        {
            MessageId = ObjectId.NewId(),
            Type = MessageType.Heartbeat,
            AppId = this.AppId,
            Body = this.NodeId
        };
        this.rabbitProducer.Publish("heartbeat", this.NodeId, message.ToJson());
    }
    private void StartConsumers()
    {
        var nodeIds = new List<string>();
        var removedKeys = new List<string>();
        foreach (var nodeId in this.nodeHeartbeats.Keys)
        {
            if (DateTime.Now.Subtract(this.nodeHeartbeats[nodeId]) > this.heartbeatCycle * 1.5)
            {
                removedKeys.Add(nodeId);
                continue;
            }
            nodeIds.Add(nodeId);
        }
        nodeIds.Sort((x, y) => x.CompareTo(y));
        if (removedKeys.Count > 0)
            removedKeys.ForEach(f => this.nodeHeartbeats.TryRemove(f, out _));

        int index = 0;
        var myNodeInfo = nodeIds.Find(f => f == this.NodeId);
        var nodeCount = nodeIds.Count;

        //先启动有状态集群
        var myClusters = this.localClusters.Where(f => f.IsEnabled && f.IsStateful)
            .OrderBy(f => f.ClusterId).ToList();

        for (int i = 0; i < myClusters.Count; i++)
        {
            var myCluster = myClusters[i];
            var clusterId = myCluster.ClusterId;
            Cluster oldCluster = null;
            if (this.lastClusters != null)
                oldCluster = this.lastClusters.Find(f => f.ClusterId == clusterId);
            var changeType = ChangeType.None;
            int oldWorkloadTotal = 0;

            //确定变更类型
            if (oldCluster != null)
            {
                oldWorkloadTotal = oldCluster.WorkloadTotal;
                //增加新队列
                if (myCluster.WorkloadTotal > oldCluster.WorkloadTotal)
                {
                    changeType = ChangeType.AddQueue;
                    for (int k = oldWorkloadTotal; k < myCluster.WorkloadTotal; k++)
                    {
                        var queueName = $"{myCluster.Queue}.{k}";
                        this.rabbitProducer.CreateQueue(queueName, true, false);
                        this.rabbitProducer.BindQueue(myCluster.ClusterId, queueName, k.ToString());
                    }
                }
                if (myCluster.WorkloadTotal < oldCluster.WorkloadTotal)
                    changeType = ChangeType.RemoveQueue;
            }
            //构造消费者
            for (int j = 0; j < myCluster.WorkloadTotal; j++)
            {
                var queueName = $"{myCluster.Queue}.{j}";
                //SingleActiveConsumer每个节点建立一个消费者，轮询分配到每个节点一个消费者              
                int needCount = 0;
                if (!this.consumers.TryGetValue(clusterId, out var rabbitConsumers))
                    this.consumers.TryAdd(clusterId, rabbitConsumers = new());
                var myRabbitConsumers = rabbitConsumers.FindAll(f => f.QueueName == queueName);
                var existedCount = myRabbitConsumers.Count;

                for (int k = 0; k < this.sacCount; k++)
                {
                    var nodeId = nodeCount > 0 ? nodeIds[index % nodeCount] : this.NodeId;
                    if (nodeId == this.NodeId)
                    {
                        needCount++;
                        index++;
                        continue;
                    }
                    index++;
                }
                if (needCount > existedCount)
                {
                    for (int k = 0; k < needCount - existedCount; k++)
                    {
                        (var parameterType, var handler) = ((Type, Func<object, Task>))this.consumerHandlers[clusterId];
                        var myRabbitConsumer = new RabbitConsumer(clusterId, queueName, this, this.serviceProvider, false, parameterType, handler) { IsLogEnabled = myCluster.IsLogEnabled };
                        rabbitConsumers.Add(myRabbitConsumer);
                        //不管是第一次还是已经存在了，新增本节点，都直接启动，因为有已经存在的消费者在消费了
                        switch (changeType)
                        {
                            case ChangeType.AddQueue:
                                if (!this.waitingStartConsumers.TryGetValue(clusterId, out var startingConsumerWaiter))
                                    this.waitingStartConsumers.TryAdd(clusterId, startingConsumerWaiter = new());
                                startingConsumerWaiter.WaitTotal = oldWorkloadTotal;
                                startingConsumerWaiter.Consumers.Add(myRabbitConsumer);
                                break;
                            case ChangeType.BindingChanged:
                                // TODO:暂时不处理这两种情况
                                break;
                            case ChangeType.None:
                            default:
                                var bindingKey = j.ToString();
                                myRabbitConsumer.Start();
                                break;
                        }
                    }
                    //新增队列时，发送消息结束标识
                    if (changeType == ChangeType.AddQueue)
                    {
                        //往前面的几个队列发送完成标志消息，当消费者收到这个消息时，可以确定新加入的队列前消息都已经消费完毕，
                        //此后新队列中的消息才可以进行消费，这样可以避免由于顺序导致并发问题
                        for (int t = 0; t < oldWorkloadTotal; t++)
                        {
                            var routingKey = t.ToString();
                            var myQueueName = $"{myCluster.Queue}.{t}";
                            var message = new Message
                            {
                                MessageId = ObjectId.NewId(),
                                Exchange = clusterId,
                                RoutingKey = routingKey,
                                Type = MessageType.WaitForStart,
                                Body = myQueueName
                            };
                            this.rabbitProducer.Publish(clusterId, routingKey, message.ToJson());
                        }
                    }
                }
                else if (needCount < existedCount)
                {
                    while (rabbitConsumers.Count > needCount)
                    {
                        var removeIndex = rabbitConsumers.Count - 1;
                        var myRabbitConsumer = rabbitConsumers[removeIndex];
                        switch (changeType)
                        {
                            case ChangeType.RemoveQueue:
                                if (!this.waitingShutdownConsumers.TryGetValue(clusterId, out var waitingConsumers))
                                    this.waitingShutdownConsumers.TryAdd(clusterId, waitingConsumers = new());
                                waitingConsumers.Add(myRabbitConsumer);
                                rabbitConsumers.RemoveAt(removeIndex);
                                var routingKey = $"{j}";
                                var message = new Message
                                {
                                    MessageId = ObjectId.NewId(),
                                    Exchange = clusterId,
                                    RoutingKey = routingKey,
                                    Type = MessageType.WaitForShutdown,
                                    Body = queueName
                                };
                                this.rabbitProducer.Publish(clusterId, routingKey, message.ToJson());
                                break;
                            case ChangeType.BindingChanged:
                                // TODO:暂时不处理这两种情况
                                break;
                            case ChangeType.None:
                            default:
                                myRabbitConsumer.Shutdown();
                                rabbitConsumers.RemoveAt(removeIndex);
                                break;
                        }
                    }
                }
            }
        }

        //再启动无状态集群
        myClusters = this.localClusters.Where(f => f.IsEnabled && !f.IsStateful)
            .OrderBy(f => f.ClusterId).ToList();
        for (int i = 0; i < myClusters.Count; i++)
        {
            var myCluster = myClusters[i];
            var clusterId = myCluster.ClusterId;
            int needCount = 0;
            if (!this.consumers.TryGetValue(clusterId, out var rabbitConsumers))
                this.consumers.TryAdd(clusterId, rabbitConsumers = new());
            var existedCount = rabbitConsumers.Count;

            for (int j = 0; j < myCluster.WorkloadTotal; j++)
            {
                var nodeId = nodeCount > 0 ? nodeIds[index % nodeCount] : this.NodeId;
                if (nodeId == this.NodeId)
                    needCount++;
                index++;
            }
            if (needCount > existedCount)
            {
                var handlerKey = $"{clusterId}-{myCluster.Queue}";
                (var parameterType, var handler) = ((Type, Func<object, Task>))this.consumerHandlers[handlerKey];

                for (int k = 0; k < needCount - existedCount; k++)
                {
                    var myRabbitConsumer = new RabbitConsumer(clusterId, myCluster.Queue, this, this.serviceProvider, false, parameterType, handler) { IsLogEnabled = myCluster.IsLogEnabled };
                    rabbitConsumers.Add(myRabbitConsumer);
                    myRabbitConsumer.Start();
                }
            }
            else if (needCount < existedCount)
            {
                while (rabbitConsumers.Count > needCount)
                {
                    var removeIndex = rabbitConsumers.Count - 1;
                    var myRabbitConsumer = rabbitConsumers[removeIndex];
                    myRabbitConsumer.Shutdown();
                    rabbitConsumers.RemoveAt(removeIndex);
                }
            }
        }
    }
}
