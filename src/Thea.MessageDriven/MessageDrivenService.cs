using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Thea.Logging;

namespace Thea.MessageDriven;

class MessageDrivenService : IMessageDriven
{
    private readonly Task task;
    private readonly TimeSpan heartbeatCycle;
    private readonly CancellationTokenSource cancellationSource = new CancellationTokenSource();
    private readonly EventWaitHandle readyToStart = new EventWaitHandle(false, EventResetMode.AutoReset);
    private readonly ConcurrentDictionary<string, List<RabbitConsumer>> consumers = new();
    private readonly ConcurrentDictionary<string, ConsumerWaiter> waitingStartConsumers = new();
    private readonly ConcurrentDictionary<string, List<RabbitConsumer>> waitingShutdownConsumers = new();
    private readonly ConcurrentDictionary<string, DateTime> nodeHeartbeats = new();
    private readonly ConcurrentDictionary<string, RpcWaiter> rpcWaiters = new();
    private readonly ConcurrentQueue<Message> messageQueue = new();

    private bool hasConsumer = false;
    private List<string> localExchangeIds = new();
    private List<string> localQueueIds = new();
    private List<Queue> queues = new();
    private List<Queue> lastQueues = null;
    private List<Binding> bindings = new();
    private List<string> rpcExchanges = new();
    internal RabbitProducer rabbitProducer;
    private RabbitConsumer heartbeatRabbitConsumer;
    private RabbitConsumer resultRabbitConsumer;
    private readonly Dictionary<string, Dictionary<string, MethodInfo>> consumerHandlers = new();
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
                    //每10秒发送一次心跳，根据配置更新本地集群配置信息localQueues，配置中心或是数据库会有更改，比如：临时禁用某个集群
                    if (DateTime.Now - this.lastInitedTime >= this.heartbeatCycle)
                    {
                        await this.Initialize();
                        if (this.hasConsumer)
                            await this.SendHeartbeat();
                        this.lastInitedTime = DateTime.Now;
                    }
                    //经过1.5个心跳后，根据前面获取的最新配置信息和最新服务器信息，启动本AppId下的所有消费者
                    if (this.hasConsumer)
                    {
                        if (DateTime.Now - this.lastUpdatedTime > this.heartbeatCycle * 1.5)
                        {
                            this.StartConsumers();
                            this.lastUpdatedTime = DateTime.Now;
                        }
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
                        string queueId = null;
                        string queueName = null;
                        TaskCompletionSource<bool> waiter = null;
                        switch (message.Type)
                        {
                            case MessageType.Message:
                            case MessageType.RpcMessage:
                                var theaMessage = new
                                {
                                    message.MessageId,
                                    message.Type,
                                    message.AppId,
                                    RoutingKey = message.Type == MessageType.RpcMessage ? this.NodeId : message.RoutingKey,
                                    message.Body
                                };
                                if (message.ScheduleTimeUtc.HasValue)
                                    this.rabbitProducer.Schedule(message.Exchange, message.RoutingKey, message.ScheduleTimeUtc.Value, theaMessage.ToJson());
                                else
                                {
                                    var myExchange = this.bindings.Find(f => f.ExchangeId == message.Exchange);
                                    if (myExchange == null)
                                        throw new Exception($"未知的交换机{message.Exchange}，请先注册交换机:{message.Exchange}，可使用UseProducer或是UseStatefulConsumer、UseSubscriber方法");
                                    var myQueue = this.queues.Find(f => f.QueueId == myExchange.QueueId);
                                    if (myQueue == null)
                                        throw new Exception($"未知的绑定队列{myExchange.QueueId}，请先注册队列及消费者，可使用UseStatefulConsumer或UseSubscriber方法");
                                    if (myQueue.IsStateful)
                                    {
                                        uint routingKey = 0;
                                        if (myQueue.WorkloadTotal > 1)
                                        {
                                            var hashKey = Farmhash.Hash32(message.RoutingKey);
                                            routingKey = (uint)(hashKey % myQueue.WorkloadTotal);
                                        }
                                        await this.rabbitProducer.Publish(message.Exchange, routingKey.ToString(), theaMessage.ToJson());
                                    }
                                    else await this.rabbitProducer.Publish(message.Exchange, message.RoutingKey, theaMessage.ToJson());
                                }
                                break;
                            case MessageType.Heartbeat:
                                //统一处理心跳，可防止并发
                                (var nodeId, waiter) = ((string, TaskCompletionSource<bool>))message.Body;
                                this.nodeHeartbeats.AddOrUpdate(nodeId, DateTime.Now, (k, o) => DateTime.Now);
                                waiter.TrySetResult(true);
                                break;
                            case MessageType.WaitForStart:
                                (queueId, queueName, waiter) = ((string, string, TaskCompletionSource<bool>))message.Body;
                                if (this.waitingStartConsumers.TryGetValue(queueId, out var consumerWaiter))
                                {
                                    if (!consumerWaiter.QueueNames.Contains(queueName))
                                        consumerWaiter.QueueNames.Add(queueName);

                                    if (consumerWaiter.QueueNames.Count >= consumerWaiter.WaitTotal)
                                    {
                                        foreach (var consumer in consumerWaiter.Consumers)
                                            await consumer.Start();
                                        this.waitingStartConsumers.TryRemove(queueId, out _);
                                    }
                                }
                                waiter.TrySetResult(true);
                                break;
                            case MessageType.WaitForShutdown:
                                (queueName, waiter) = ((string, TaskCompletionSource<bool>))message.Body;
                                if (this.waitingShutdownConsumers.TryRemove(queueName, out var rabbitConsumers))
                                    rabbitConsumers.ForEach(f => f.Shutdown());
                                waiter.TrySetResult(true);
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
        this.rabbitProducer.Shutdown().Wait();
        foreach (var rabbitConsumers in this.consumers.Values)
            rabbitConsumers.ForEach(f => f.Shutdown());
        this.consumers.Clear();

        if (this.task != null)
            this.task.Wait();
        this.cancellationSource.Dispose();
    }
    public void Publish<TMessage>(string exchange, string routingKey, TMessage message)
    {
        if (!this.bindings.Exists(f => f.ExchangeId == exchange))
            throw new Exception($"交换机{exchange}没有绑定到任何队列，本地没有绑定或是没有从持久化存储中捞取到绑定关系数据");
        if (message == null)
            throw new ArgumentNullException(nameof(message));

        this.messageQueue.Enqueue(new Message
        {
            MessageId = ObjectId.NewId(),
            AppId = this.AppId,
            Type = MessageType.Message,
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
    public string Request<TMessage>(string exchange, string routingKey, TMessage message)
    {
        if (!this.bindings.Exists(f => f.ExchangeId == exchange))
            throw new Exception($"交换机{exchange}没有绑定到任何队列，本地没有绑定或是没有从持久化存储中捞取到绑定关系数据");
        if (!this.rpcExchanges.Contains(exchange))
            throw new Exception($"当前交换机{exchange}并没有配置RPC模式，可使用方法：UseProducer(exchange, isUseRpc, isDelay)，isUseRpc设置为true");
        if (message == null)
            throw new ArgumentNullException(nameof(message));

        var theaMessage = new Message
        {
            MessageId = ObjectId.NewId(),
            AppId = this.AppId,
            Type = MessageType.RpcMessage,
            Exchange = exchange,
            RoutingKey = routingKey,
            Body = message.ToJson()
        };
        var rpcWaiter = new RpcWaiter { MessageId = theaMessage.MessageId };
        this.rpcWaiters.TryAdd(theaMessage.MessageId, rpcWaiter);
        this.messageQueue.Enqueue(theaMessage);
        return rpcWaiter.Waiter.Task.Result;
    }
    public async Task<string> RequestAsync<TMessage>(string exchange, string routingKey, TMessage message)
    {
        if (!this.bindings.Exists(f => f.ExchangeId == exchange))
            throw new Exception($"交换机{exchange}没有绑定到任何队列，本地没有绑定或是没有从持久化存储中捞取到绑定关系数据");
        if (!this.rpcExchanges.Contains(exchange))
            throw new Exception($"当前交换机{exchange}并没有配置RPC模式，可使用方法：UseProducer(exchange, isUseRpc, isDelay)，isUseRpc设置为true");
        if (message == null)
            throw new ArgumentNullException(nameof(message));

        var theaMessage = new Message
        {
            MessageId = ObjectId.NewId(),
            AppId = this.AppId,
            Type = MessageType.RpcMessage,
            Exchange = exchange,
            RoutingKey = routingKey,
            Body = message.ToJson()
        };
        var rpcWaiter = new RpcWaiter { MessageId = theaMessage.MessageId };
        this.rpcWaiters.TryAdd(theaMessage.MessageId, rpcWaiter);
        this.messageQueue.Enqueue(theaMessage);
        return await rpcWaiter.Waiter.Task;
    }
    public void Schedule<TMessage>(string exchange, string routingKey, TMessage message, DateTime enqueueTimeUtc)
    {
        if (enqueueTimeUtc < DateTime.UtcNow)
            throw new Exception($"入队时间晚于现在时间，只能选择未来时间");
        var myExchange = this.bindings.Find(f => f.ExchangeId == exchange);
        if (myExchange == null)
            throw new Exception($"未知的交换机{exchange}，请先注册交换机:{exchange}，可使用UseProducer或是UseStatefulConsumer、UseSubscriber方法");
        if (message == null)
            throw new ArgumentNullException(nameof(message));

        this.messageQueue.Enqueue(new Message
        {
            MessageId = ObjectId.NewId(),
            AppId = this.AppId,
            Type = MessageType.Message,
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
            if (this.localExchangeIds.Exists(f => f == exchange))
                continue;
            this.localExchangeIds.Add(exchange);
        }
    }
    public void UseProducer(string exchange, bool isUseRpc)
    {
        if (!this.localExchangeIds.Contains(exchange))
            this.localExchangeIds.Add(exchange);
        if (isUseRpc && !this.rpcExchanges.Contains(exchange))
            this.rpcExchanges.Add(exchange);
    }
    public void UseStatefulConsumer(string exchange, string queue, MethodInfo methodInfo)
    {
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
                IsStateful = true,
                IsSac = true,
                PrefetchCount = 250,
                WorkloadTotal = 2,
                IsEnabled = true,
                IsLogEnabled = false
            });
        }
        else
        {
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
                QueueId = queue
            });
        }
        else myBinding.BindType = Consts.TopicBindingType;
    }
    public void UseSubscriber(string exchange, string queue, MethodInfo methodInfo, string routingKey = "#", bool isDelay = false)
    {
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
                IsStateful = false,
                IsSac = false,
                PrefetchCount = 5,
                WorkloadTotal = 2,
                IsEnabled = true,
                IsLogEnabled = false
            });
        }
        else
        {
            myQueue.IsStateful = false;
            myQueue.IsSac = false;
            myQueue.PrefetchCount = 5;
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
                IsDelay = isDelay
            });
        }
        else
        {
            myBinding.BindType = bindingType;
            myBinding.BindingKey = routingKey;
            myBinding.IsDelay = isDelay;
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
    internal void Next(string messageId, string result)
    {
        if (this.rpcWaiters.TryRemove(messageId, out var rpcWaiter))
            rpcWaiter.Waiter.TrySetResult(result);
    }
    private async Task Register()
    {
        //捞取数据库或是配置中心的集群信息        
        (var dbQueues, var dbBindings) = await this.repository.GetConfigInfo();
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

        this.rabbitProducer = await RabbitProducer.Create(this, this.serviceProvider);
        if (this.localExchangeIds.Count > 0)
        {
            var exchange = Consts.RpcExchange;
            var rpcQueueName = $"rpc.result.{this.NodeId}";
            await this.rabbitProducer.CreateExchange(exchange, Consts.TopicBindingType);
            this.resultRabbitConsumer = new RabbitConsumer(rpcQueueName, this, this.serviceProvider, true);
            await this.resultRabbitConsumer.Start(exchange, this.NodeId);
        }

        //没有消费者，什么都不做，也不创建
        if (!this.hasConsumer) return;
        await this.rabbitProducer.CreateExchange(Consts.HeartbeatExchange, Consts.TopicBindingType);
        var queueName = $"heartbeat.queue.{this.NodeId}";
        this.heartbeatRabbitConsumer = new RabbitConsumer(queueName, this, this.serviceProvider, true);
        await this.heartbeatRabbitConsumer.Start(Consts.HeartbeatExchange, Consts.SubscriberRoutingKey);

        //创建交换机和队列
        foreach (var queue in this.queues)
        {
            if (!queue.IsEnabled)
                continue;

            var myBindings = this.bindings.FindAll(f => f.QueueId == queue.QueueId);
            foreach (var myBinding in myBindings)
            {
                await rabbitProducer.CreateExchange(myBinding.ExchangeId, myBinding.BindType, myBinding.IsDelay);
                Console.WriteLine($"Exchange: {queue.QueueId} is created");
            }

            if (queue.IsStateful)
            {
                for (int i = 0; i < queue.WorkloadTotal; i++)
                {
                    queueName = $"{queue.QueueId}.{i}";
                    await this.rabbitProducer.CreateQueue(queueName, queue.IsSac, false);
                    Console.WriteLine($"Stateful consumer queue: {queue.QueueId} is created");
                    foreach (var myBinding in myBindings)
                    {
                        var bindingKey = i.ToString();
                        await this.rabbitProducer.BindQueue(myBinding.ExchangeId, queueName, bindingKey);
                        Console.WriteLine($"Exchange: {myBinding.ExchangeId}, BindType: {myBinding.BindType}, BindingKey: {bindingKey}, Queue: {queueName}");
                    }
                }
            }
            else
            {
                await this.rabbitProducer.CreateQueue(queue.QueueId, false, false);
                Console.WriteLine($"Subscriber queue: {queue.QueueId} is created");
                foreach (var myBinding in myBindings)
                {
                    await this.rabbitProducer.BindQueue(myBinding.ExchangeId, queue.QueueId, Consts.SubscriberRoutingKey);
                    Console.WriteLine($"Exchange: {myBinding.ExchangeId}, BindType: {myBinding.BindType}, BindingKey: #, Queue: {queue.QueueId}");
                }
            }
        }
        await this.SendHeartbeat();
        this.queues = dbQueues;
        this.bindings = dbBindings;
    }
    private async Task Initialize() => (this.queues, this.bindings)
        = await this.repository.GetConfigInfo();
    private async Task SendHeartbeat()
    {
        await this.rabbitProducer.Publish(Consts.HeartbeatExchange, this.NodeId, new Message
        {
            MessageId = ObjectId.NewId(),
            Type = MessageType.Heartbeat,
            AppId = this.AppId,
            Body = this.NodeId
        }.ToJson());
    }
    private async void StartConsumers()
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

        //优先启动有状态队列消费者
        var myQueues = this.queues.Where(f => this.localQueueIds.Contains(f.QueueId) && f.IsEnabled && f.IsStateful)
            .OrderBy(f => f.QueueId).ToList();

        for (int i = 0; i < myQueues.Count; i++)
        {
            var myQueue = myQueues[i];
            var queueId = myQueue.QueueId;
            Queue oldQueue = null;
            if (this.lastQueues != null)
                oldQueue = this.lastQueues.Find(f => f.QueueId == queueId);
            int oldWorkloadTotal = 0;
            var changeType = ChangeType.None;
            if (oldQueue != null)
            {
                oldWorkloadTotal = oldQueue.WorkloadTotal;
                //有新增队列就创建并绑定
                if (myQueue.WorkloadTotal > oldQueue.WorkloadTotal)
                {
                    changeType = ChangeType.AddQueue;
                    for (int k = oldWorkloadTotal; k < myQueue.WorkloadTotal; k++)
                    {
                        var queueName = $"{queueId}.{k}";
                        await this.rabbitProducer.CreateQueue(queueName, true, false);
                        await this.rabbitProducer.BindQueue(myQueue.QueueId, queueName, k.ToString());
                    }
                }
                else if (myQueue.WorkloadTotal < oldQueue.WorkloadTotal)
                    changeType = ChangeType.RemoveQueue;
            }

            if (!this.consumers.TryGetValue(queueId, out var rabbitConsumers))
                this.consumers.TryAdd(queueId, rabbitConsumers = new());

            //构造消费者
            for (int j = 0; j < myQueue.WorkloadTotal; j++)
            {
                var queueName = $"{queueId}.{j}";
                //SingleActiveConsumer每个节点建立一个消费者，轮询分配到每个节点一个消费者              
                int needCount = 0;
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
                        var exchangeHandlers = this.consumerHandlers[queueId];
                        var myRabbitConsumer = new RabbitConsumer(queueName, this, this.serviceProvider, false, exchangeHandlers) { IsLogEnabled = myQueue.IsLogEnabled };
                        rabbitConsumers.Add(myRabbitConsumer);

                        //不管是第一次还是已经存在了，新增本节点，都直接启动，因为有已经存在的消费者在消费了
                        if (changeType == ChangeType.AddQueue && j >= oldWorkloadTotal)
                        {
                            //新增加的队列消费者，需要等待前面的几个队列消费完毕后再启动，避免消息顺序错乱问题
                            if (!this.waitingStartConsumers.TryGetValue(queueId, out var startingConsumerWaiter))
                                this.waitingStartConsumers.TryAdd(queueId, startingConsumerWaiter = new());
                            startingConsumerWaiter.WaitTotal = oldWorkloadTotal;
                            startingConsumerWaiter.Consumers.Add(myRabbitConsumer);
                        }
                        //节点偏移或是新启动，直接启动
                        else await myRabbitConsumer.Start();
                    }
                }
                else if (needCount < existedCount)
                {
                    while (rabbitConsumers.Count > needCount)
                    {
                        var removeIndex = rabbitConsumers.Count - 1;
                        var myRabbitConsumer = rabbitConsumers[removeIndex];
                        //增加节点导致本节点消费者减少，当前消息消费完，就直接关闭，会有其他SAC消费者继续消费
                        myRabbitConsumer.Shutdown();
                        rabbitConsumers.RemoveAt(removeIndex);
                    }
                }
            }
            if (changeType == ChangeType.AddQueue)
            {
                //往前面的几个队列发送结束标志消息，当消费者收到这个消息时，可以确定新加入的队列前消息都已经消费完毕，
                //此后新队列中的消息才可以进行消费，这样可以避免消息顺序错乱问题
                for (int j = 0; j < oldWorkloadTotal; j++)
                {
                    var exchange = Consts.DefaultExchange;
                    var queueName = $"{queueId}.{j}";
                    var message = new Message
                    {
                        MessageId = ObjectId.NewId(),
                        AppId = this.AppId,
                        Exchange = exchange,
                        RoutingKey = queueName,
                        Type = MessageType.WaitForStart,
                        Body = (queueId, queueName)
                    };
                    //使用默认的交换机，路由键为队列名
                    await this.rabbitProducer.Publish(exchange, queueName, message.ToJson());
                }
            }
            else if (changeType == ChangeType.RemoveQueue)
            {
                //先移除本地的消费者不关闭，等待收到这些消费者的消息消费完毕的结束标志消息后，再关闭
                for (int j = myQueue.WorkloadTotal; j < oldWorkloadTotal; j++)
                {
                    var queueName = $"{queueId}.{j}";
                    //关闭队列消费者，按照实际子队列来进行关闭
                    if (!this.waitingShutdownConsumers.TryGetValue(queueName, out var waitingConsumers))
                        this.waitingShutdownConsumers.TryAdd(queueId, waitingConsumers = new());
                    var myRabbitConsumers = rabbitConsumers.FindAll(f => f.QueueName == queueName);
                    foreach (var myRabbitConsumer in myRabbitConsumers)
                    {
                        waitingConsumers.Add(myRabbitConsumer);
                        rabbitConsumers.Remove(myRabbitConsumer);
                    }
                    var exchange = Consts.DefaultExchange;
                    var message = new Message
                    {
                        MessageId = ObjectId.NewId(),
                        AppId = this.AppId,
                        Exchange = exchange,
                        RoutingKey = queueName,
                        Type = MessageType.WaitForShutdown,
                        Body = queueName
                    };
                    await this.rabbitProducer.Publish(exchange, queueName, message.ToJson());
                }
            }
        }

        //再启动无状态队列消费者
        myQueues = this.queues.Where(f => this.localQueueIds.Contains(f.QueueId) && f.IsEnabled && !f.IsStateful)
            .OrderBy(f => f.QueueId).ToList();
        for (int i = 0; i < myQueues.Count; i++)
        {
            var myQueue = myQueues[i];
            var queueId = myQueue.QueueId;
            int needCount = 0;
            if (!this.consumers.TryGetValue(queueId, out var rabbitConsumers))
                this.consumers.TryAdd(queueId, rabbitConsumers = new());
            var existedCount = rabbitConsumers.Count;

            for (int j = 0; j < myQueue.WorkloadTotal; j++)
            {
                var nodeId = nodeCount > 0 ? nodeIds[index % nodeCount] : this.NodeId;
                if (nodeId == this.NodeId)
                    needCount++;
                index++;
            }
            if (needCount > existedCount)
            {
                var exchangeHandlers = this.consumerHandlers[queueId];
                for (int k = 0; k < needCount - existedCount; k++)
                {
                    var myRabbitConsumer = new RabbitConsumer(myQueue.QueueId, this, this.serviceProvider, false, exchangeHandlers) { IsLogEnabled = myQueue.IsLogEnabled };
                    rabbitConsumers.Add(myRabbitConsumer);
                    await myRabbitConsumer.Start();
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