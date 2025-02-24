using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections;
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
    private readonly CancellationTokenSource cancellationSource = new();
    private readonly EventWaitHandle readyToStart = new EventWaitHandle(false, EventResetMode.AutoReset);
    private readonly ConcurrentDictionary<string, List<RabbitConsumer>> consumers = new();
    private readonly ConcurrentDictionary<string, ConsumerWaiter> waitingStartConsumers = new();
    private readonly ConcurrentDictionary<string, List<RabbitConsumer>> waitingShutdownConsumers = new();
    private readonly ConcurrentDictionary<string, DateTime> heartbeats = new();
    private readonly ConcurrentDictionary<string, RpcWaiter> rpcWaiters = new();
    private readonly ConcurrentQueue<Message> messageQueue = new();

    private bool hasConsumer = false;
    private List<string> localExchangeIds = new();
    private List<string> localQueueIds = new();
    private List<Queue> queues = new();
    private List<Queue> lastQueues = null;
    private List<Binding> bindings = new();
    private List<string> rpcExchanges = new();
    private RabbitConsumer heartbeatRabbitConsumer;
    private RabbitConsumer resultRabbitConsumer;
    private readonly Dictionary<string, Dictionary<string, MethodInfo>> consumerHandlers = new();
    private readonly Dictionary<string, ExchangeRoutingSelector> exchangeRoutingSelectors = new();
    private readonly IServiceProvider serviceProvider;
    private readonly ILogger<MessageDrivenService> logger;
    private IMessageDrivenRepository repository;
    private DateTime lastInitedTime = DateTime.MinValue;
    private DateTime lastLoggedTime = DateTime.MinValue;
    private int sacCount = 3;

    internal RabbitProducer rabbitProducer;
    public string AppId { get; private set; }
    public string NodeId { get; private set; }

    public MessageDrivenService(IServiceProvider serviceProvider)
    {
        this.serviceProvider = serviceProvider;
        var hostAppLifetime = serviceProvider.GetService<IHostApplicationLifetime>();
        hostAppLifetime.ApplicationStopping.Register(() => this.Shutdown());

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
                                    var myBindings = this.bindings.FindAll(f => f.ExchangeId == message.Exchange);
                                    foreach (var myBinding in myBindings)
                                    {
                                        var myQueue = this.queues.Find(f => f.QueueId == myBinding.QueueId);
                                        if (myQueue == null)
                                        {
                                            (this.queues, this.bindings) = await this.repository.GetConfigInfo();
                                            myQueue = this.queues.Find(f => f.QueueId == myBinding.QueueId);
                                            if (myQueue == null)
                                            {
                                                var errMessage = $"未注册的队列{myBinding.QueueId}，可使用UseStatefulConsumer、UseSubscriber方法进行注册和绑定";
                                                this.logger.LogTagError("MessageDriven", errMessage);
                                                throw new Exception(errMessage);
                                            }
                                        }
                                        if (myQueue.IsStateful)
                                        {
                                            uint routingKey = 0;
                                            if (myQueue.WorkloadTotal > 1)
                                            {
                                                var hashKey = Farmhash.Hash32(message.RoutingKey);
                                                routingKey = (uint)(hashKey % myQueue.WorkloadTotal);
                                            }
                                            Console.WriteLine($"Exchange: {message.Exchange}  RoutingKey: {routingKey} to queue: {myQueue.QueueId}");
                                            await this.rabbitProducer.Publish(message.Exchange, routingKey.ToString(), theaMessage.ToJson());
                                        }
                                        else
                                        {
                                            await this.rabbitProducer.Publish(message.Exchange, message.RoutingKey, theaMessage.ToJson());
                                            Console.WriteLine($"Exchange: {message.Exchange}  RoutingKey: {message.RoutingKey} to queue: {myQueue.QueueId}");
                                        }
                                    }
                                }
                                break;
                            case MessageType.Heartbeat:
                                //统一处理心跳，可防止并发
                                (var nodeId, waiter) = ((string, TaskCompletionSource<bool>))message.Body;
                                this.heartbeats.AddOrUpdate(nodeId, DateTime.Now, (k, o) => DateTime.Now);
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
                                    rabbitConsumers.ForEach(async f => await f.Shutdown());
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
        this.heartbeats.TryAdd(this.NodeId, DateTime.Now);
        this.readyToStart.Set();
        Console.WriteLine($"Local NodeId: {this.NodeId}");
    }
    public void Shutdown()
    {
        this.cancellationSource.Cancel();
        this.rabbitProducer.Shutdown().Wait();
        foreach (var rabbitConsumers in this.consumers.Values)
            rabbitConsumers.ForEach(async f => await f.Shutdown());
        this.consumers.Clear();
        this.rabbitProducer.Shutdown().Wait();
        this.heartbeatRabbitConsumer?.Shutdown().Wait();
        this.resultRabbitConsumer?.Shutdown();

        if (this.task != null)
            this.task.Wait();
        this.cancellationSource.Dispose();
    }
    public void Publish<TMessage>(string exchange, string routingKey, TMessage message)
    {
        if (!this.bindings.Exists(f => f.ExchangeId == exchange))
        {
            var errMessage = $"未注册的交换机{exchange}，可使用UseProducer或是UseStatefulConsumer、UseSubscriber方法进行注册，但必须有应用使用UseStatefulConsumer、UseSubscriber方法进行绑定";
            this.logger.LogTagError("MessageDriven", errMessage);
            throw new Exception(errMessage);
        }
        if (message == null)
            throw new ArgumentNullException(nameof(message));
        if (this.exchangeRoutingSelectors.TryGetValue(exchange, out var exchangeRoutingSelector))
            (exchange, routingKey) = exchangeRoutingSelector(exchange, routingKey, message);
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
        {
            var errMessage = $"未注册的交换机{exchange}，可使用UseProducer或是UseStatefulConsumer、UseSubscriber方法进行注册，但必须有应用使用UseStatefulConsumer、UseSubscriber方法进行绑定";
            this.logger.LogTagError("MessageDriven", errMessage);
            throw new Exception(errMessage);
        }
        if (!this.rpcExchanges.Contains(exchange))
        {
            var errMessage = $"当前交换机{exchange}并没有配置RPC模式，可使用方法：UseProducer(exchange, isUseRpc, isDelay)，isUseRpc设置为true";
            this.logger.LogTagError("MessageDriven", errMessage);
            throw new Exception(errMessage);
        }
        if (message == null)
            throw new ArgumentNullException(nameof(message));

        if (this.exchangeRoutingSelectors.TryGetValue(exchange, out var exchangeRoutingSelector))
            (exchange, routingKey) = exchangeRoutingSelector(exchange, routingKey, message);
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
        {
            var errMessage = $"未注册的交换机{exchange}，可使用UseProducer或是UseStatefulConsumer、UseSubscriber方法进行注册，但必须有应用使用UseStatefulConsumer、UseSubscriber方法进行绑定";
            this.logger.LogTagError("MessageDriven", errMessage);
            throw new Exception(errMessage);
        }
        if (!this.rpcExchanges.Contains(exchange))
        {
            var errMessage = $"当前交换机{exchange}并没有配置RPC模式，可使用方法：UseProducer(exchange, isUseRpc, isDelay)，isUseRpc设置为true";
            this.logger.LogTagError("MessageDriven", errMessage);
            throw new Exception(errMessage);
        }
        if (message == null)
            throw new ArgumentNullException(nameof(message));

        if (this.exchangeRoutingSelectors.TryGetValue(exchange, out var exchangeRoutingSelector))
            (exchange, routingKey) = exchangeRoutingSelector(exchange, routingKey, message);
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
        if (!this.bindings.Exists(f => f.ExchangeId == exchange))
        {
            var errMessage = $"未注册的交换机{exchange}，可使用UseProducer或是UseStatefulConsumer、UseSubscriber方法进行注册，但必须有应用使用UseStatefulConsumer、UseSubscriber方法进行绑定";
            this.logger.LogTagError("MessageDriven", errMessage);
            throw new Exception(errMessage);
        }
        if (message == null)
            throw new ArgumentNullException(nameof(message));

        if (this.exchangeRoutingSelectors.TryGetValue(exchange, out var exchangeRoutingSelector))
            (exchange, routingKey) = exchangeRoutingSelector(exchange, routingKey, message);
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
    public async Task ChangeQueue(string queueId, int workloadTotal)
    {
        var myQueue = this.queues.Find(f => f.QueueId == queueId);
        var oldWorkloadTotal = myQueue.WorkloadTotal;
        if (myQueue == null || !myQueue.IsEnabled || !myQueue.IsStateful
            || oldWorkloadTotal == workloadTotal) return;
        if (workloadTotal > oldWorkloadTotal)
        {
            var myBindings = this.bindings.FindAll(f => f.QueueId == queueId);
            for (int i = oldWorkloadTotal; i < workloadTotal; i++)
            {
                var queueName = $"{queueId}.{i}";
                await this.rabbitProducer.CreateQueue(queueName, myQueue.IsSac, false);
                foreach (var myBinding in myBindings)
                    await this.rabbitProducer.BindQueue(myBinding.ExchangeId, queueName, i.ToString());
            }
        }
        await this.repository.ChangeQueue(queueId, workloadTotal);
    }
    public void UseStrategy(string exchange, ExchangeRoutingSelector exchangeRoutingKeySelector)
    {
        if (exchangeRoutingKeySelector == null)
            throw new ArgumentNullException(nameof(exchangeRoutingKeySelector));
        this.exchangeRoutingSelectors.Add(exchange, exchangeRoutingKeySelector);
    }
    internal void UseRepository(IMessageDrivenRepository repository) => this.repository = repository;
    internal void AddLogs(ExecLog logInfo) => this.messageQueue.Enqueue(new Message
    {
        MessageId = ObjectId.NewId(),
        Type = MessageType.Logs,
        Body = logInfo
    });
    internal void ProcessMessage(Message message) => this.messageQueue.Enqueue(message);
    internal void SetRpcResult(string messageId, string result)
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
        if (!this.hasConsumer) return;

        //消费者先把队列和绑定建好后，生产者再变更
        (this.queues, this.bindings) = await this.repository.GetConfigInfo();
        await this.rabbitProducer.CreateExchange(Consts.HeartbeatExchange, Consts.TopicBindingType);
        var queueName = $"heartbeat.queue.{this.NodeId}";
        this.heartbeatRabbitConsumer = new RabbitConsumer(queueName, this, this.serviceProvider, true);
        await this.heartbeatRabbitConsumer.Start(Consts.HeartbeatExchange, Consts.FanoutRoutingKey);

        //创建交换机和队列
        foreach (var queue in this.queues)
        {
            if (!queue.IsEnabled)
                continue;

            var myBindings = this.bindings.FindAll(f => f.QueueId == queue.QueueId);
            foreach (var myBinding in myBindings)
                await rabbitProducer.CreateExchange(myBinding.ExchangeId, myBinding.BindType, myBinding.IsDelay);

            if (queue.IsStateful)
            {
                for (int i = 0; i < queue.WorkloadTotal; i++)
                {
                    queueName = $"{queue.QueueId}.{i}";
                    await this.rabbitProducer.CreateQueue(queueName, queue.IsSac, false);
                    foreach (var myBinding in myBindings)
                        await this.rabbitProducer.BindQueue(myBinding.ExchangeId, queueName, i.ToString());
                }
            }
            else
            {
                await this.rabbitProducer.CreateQueue(queue.QueueId, false, false);
                foreach (var myBinding in myBindings)
                    await this.rabbitProducer.BindQueue(myBinding.ExchangeId, queue.QueueId, Consts.FanoutRoutingKey);
            }
        }
        await this.SendHeartbeat();
    }
    private async Task Initialize()
    {
        this.lastQueues = this.queues;
        (this.queues, this.bindings) = await this.repository.GetConfigInfo();
    }
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
    private async Task StartConsumers()
    {
        var nodeIds = new List<string>();
        var removedKeys = new List<string>();
        foreach (var nodeId in this.heartbeats.Keys)
        {
            if (DateTime.Now.Subtract(this.heartbeats[nodeId]) > this.heartbeatCycle * 1.5)
            {
                removedKeys.Add(nodeId);
                continue;
            }
            nodeIds.Add(nodeId);
        }
        nodeIds.Sort((x, y) => x.CompareTo(y));
        if (removedKeys.Count > 0)
            removedKeys.ForEach(f => this.heartbeats.TryRemove(f, out _));

        int index = 0;
        var myNodeInfo = nodeIds.Find(f => f == this.NodeId);
        var nodeCount = nodeIds.Count;

        //优先启动有状态队列消费者
        var myQueues = this.queues.Where(f => this.localQueueIds.Contains(f.QueueId) && f.IsEnabled && f.IsStateful)
            .OrderBy(f => f.QueueId).ToList();

        bool isChanged = false;
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
                    changeType = ChangeType.AddQueue;
                else if (myQueue.WorkloadTotal < oldQueue.WorkloadTotal)
                    changeType = ChangeType.RemoveQueue;
            }
            if (changeType != ChangeType.None)
                isChanged = true;

            //确保所有队列都已经创建并绑定
            var myBindings = this.bindings.FindAll(f => f.QueueId == queueId);
            for (int k = oldWorkloadTotal; k < myQueue.WorkloadTotal; k++)
            {
                var queueName = $"{queueId}.{k}";
                await this.rabbitProducer.CreateQueue(queueName, true, false);
                foreach (var myBinding in myBindings)
                    await this.rabbitProducer.BindQueue(myBinding.ExchangeId, queueName, k.ToString());
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
                        await myRabbitConsumer.Shutdown();
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
                        Body = queueName
                    };
                    //使用默认的交换机，路由键为队列名
                    await this.rabbitProducer.Publish(exchange, queueName, message.ToJson());
                    Console.WriteLine($"AddQueue: {queueId},Send message to {queueName}");
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
                        this.waitingShutdownConsumers.TryAdd(queueName, waitingConsumers = new());
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
                    Console.WriteLine($"RemoveQueue: {queueId},Send message to {queueName}");
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
                    await myRabbitConsumer.Shutdown();
                    rabbitConsumers.RemoveAt(removeIndex);
                }
            }
        }

        //清除缓存，保证生产者获得配置与消费者一致
        if (isChanged) await this.repository.UpdateCache();

        Console.WriteLine($"Nodes: {string.Join(" , ", nodeIds)}");
        Console.WriteLine("Consumers:");
        foreach (var item in this.consumers)
        {
            var queues = string.Join(" , ", item.Value.Select(f => f.QueueName + ":" + f.IsActivated).ToList());
            Console.WriteLine($"QueueId: {item.Key}, Count: {item.Value.Count}, {queues}");
        }
    }
}