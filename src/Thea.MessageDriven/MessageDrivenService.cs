using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Thea.Orm;

namespace Thea.MessageDriven;

class MessageDrivenService : IMessageDriven
{
    private readonly Task task;
    private readonly CancellationTokenSource cancellationSource = new CancellationTokenSource();
    private readonly EventWaitHandle readyToStart = new EventWaitHandle(false, EventResetMode.AutoReset);
    private readonly List<string> localClusterIds = new();
    private readonly ConcurrentQueue<TheaMessage> messageQueue = new();
    private readonly ConcurrentDictionary<string, Cluster> clusters = new();
    //Key=exchang
    private readonly ConcurrentDictionary<string, ProducerInfo> producers = new();
    //Key=clusterId
    private readonly ConcurrentDictionary<string, List<ConsumerInfo>> consumers = new();
    private readonly ConcurrentDictionary<string, Func<TheaMessage, Task<TheaResponse>>> consumerHandlers = new();
    //Key=exchange, cluster.result
    private readonly ConcurrentDictionary<string, RabbitConsumer> replyConsumers = new();
    private readonly ConcurrentDictionary<string, TheaMessage> messageResults = new();
    private readonly IServiceProvider serviceProvider;
    private readonly ILogger<MessageDrivenService> logger;

    internal ClusterRepository repository;
    private DateTimeOffset lastInitedTime = DateTimeOffset.MinValue;
    private DateTimeOffset lastUpdateTime = DateTimeOffset.MinValue;

    public string NodeId { get; set; }
    public string DbKey { get; set; }

    public MessageDrivenService(IServiceProvider serviceProvider)
    {
        this.NodeId = Environment.GetEnvironmentVariable("NodeId");
        this.serviceProvider = serviceProvider;
        this.logger = serviceProvider.GetService<ILogger<MessageDrivenService>>();

        this.task = Task.Factory.StartNew(async () =>
        {
            this.readyToStart.WaitOne();

            if (string.IsNullOrEmpty(this.NodeId))
                this.NodeId = Dns.GetHostName();
            if (string.IsNullOrEmpty(this.DbKey))
            {
                this.logger.LogError("MessageDriven", "未设置dbKey,无法初始化MessageDrivenService对象");
                throw new Exception("未设置dbKey,无法初始化MessageDrivenService对象");
            }
            while (!this.cancellationSource.IsCancellationRequested)
            {
                try
                {
                    //每1分钟更新一次链接信息
                    if (DateTime.Now - this.lastInitedTime > TimeSpan.FromSeconds(30))
                    {
                        await this.Initialize();
                        //确保Consumer是活的
                        this.EnsureAvailable();
                        this.lastInitedTime = DateTime.Now;
                    }
                    if (DateTime.Now - this.lastUpdateTime > TimeSpan.FromSeconds(10))
                    {
                        await this.repository.Update(this.NodeId);
                        this.lastUpdateTime = DateTime.Now;
                    }
                    if (this.messageQueue.TryDequeue(out var message))
                    {
                        if (!this.producers.TryGetValue(message.Exchange, out var producerInfo))
                            throw new Exception($"未知的交换机{message.Exchange}，请先注册集群和生产者");

                        var routingKey = HashCode.Combine(message.RoutingKey) % producerInfo.ConsumerTotalCount;
                        producerInfo.RabbitProducer.TryPublish(message.Exchange, routingKey.ToString(), message.ToJson());
                    }
                    else Thread.Sleep(1);
                }
                catch (Exception ex)
                {
                    this.logger.LogError(ex, "MessageDriven:消费者守护宿主线程执行异常");
                }
            }
        }, this.cancellationSource.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    public void Start()
    {
        var dbFactory = this.serviceProvider.GetService<IOrmDbFactory>();
        this.repository = new ClusterRepository(dbFactory, this.DbKey);
        this.readyToStart.Set();
    }
    public void Shutdown()
    {
        this.cancellationSource.Cancel();
        if (this.task != null)
            this.task.Wait();
        this.cancellationSource.Dispose();
    }
    public TheaResponse Request<TMessage>(string exchange, string routingKey, TMessage message)
    {
        var theaMessage = new TheaMessage
        {
            MessageId = ObjectId.NewId(),
            NodeId = this.NodeId,
            Exchange = exchange,
            ReplyExchange = exchange + ".result",
            RoutingKey = routingKey,
            Message = message.ToJson(),
            Status = MessageStatus.WaitForReply,
            Waiter = new TaskCompletionSource<TheaResponse>()
        };
        this.messageResults.TryAdd(theaMessage.MessageId, theaMessage);
        this.messageQueue.Enqueue(theaMessage);
        return theaMessage.Waiter.Task.Result;
    }
    public async Task<TheaResponse> RequestAsync<TMessage>(string exchange, string routingKey, TMessage message)
    {
        var theaMessage = new TheaMessage
        {
            MessageId = ObjectId.NewId(),
            NodeId = this.NodeId,
            Exchange = exchange,
            ReplyExchange = exchange + ".result",
            RoutingKey = routingKey,
            Message = message.ToJson(),
            Status = MessageStatus.WaitForReply,
            Waiter = new TaskCompletionSource<TheaResponse>()
        };
        this.messageResults.TryAdd(theaMessage.MessageId, theaMessage);
        this.messageQueue.Enqueue(theaMessage);
        return await theaMessage.Waiter.Task;
    }
    public void Publish<TMessage>(string exchange, string routingKey, TMessage message)
    {
        if (!this.producers.TryGetValue(exchange, out var producerInfo))
            throw new Exception($"未知的交换机{exchange}，请先注册集群和生产者");
        var theaMessage = new TheaMessage
        {
            MessageId = ObjectId.NewId(),
            NodeId = this.NodeId,
            ClusterId = producerInfo.ClusterId,
            Exchange = exchange,
            RoutingKey = routingKey,
            Message = message.ToJson(),
            Status = MessageStatus.None
        };
        this.messageQueue.Enqueue(theaMessage);
    }
    public Task PublishAsync<TMessage>(string exchange, string routingKey, TMessage message)
    {
        this.Publish<TMessage>(exchange, routingKey, message);
        return Task.CompletedTask;
    }
    public async Task<TheaResponse[]> RequestAsync<TMessage>(string exchange, List<TMessage> messages, Func<TMessage, string> groupRoutingKeySelector)
    {
        if (messages == null || messages.Count == 0)
            throw new ArgumentNullException(nameof(messages));
        if (groupRoutingKeySelector == null)
            throw new ArgumentNullException(nameof(groupRoutingKeySelector));

        if (!this.producers.TryGetValue(exchange, out var producerInfo))
            throw new Exception($"未知的交换机{exchange}，请先注册集群和生产者");

        var results = new List<Task<TheaResponse>>();
        foreach (var message in messages)
        {
            var routingKeyValue = groupRoutingKeySelector.Invoke(message);
            var routingKey = HashCode.Combine(routingKeyValue) % producerInfo.ConsumerTotalCount;
            var theaMessage = new TheaMessage
            {
                MessageId = ObjectId.NewId(),
                NodeId = this.NodeId,
                ClusterId = producerInfo.ClusterId,
                Exchange = exchange,
                IsGroupMessage = true,
                RoutingKey = routingKey.ToString(),
                Message = message.ToJson(),
                Status = MessageStatus.WaitForReply,
                Waiter = new TaskCompletionSource<TheaResponse>()
            };
            this.messageResults.TryAdd(theaMessage.MessageId, theaMessage);
            this.messageQueue.Enqueue(theaMessage);
            results.Add(theaMessage.Waiter.Task);
        }
        return await Task.WhenAll(results);
    }
    public TheaResponse[] Request<TMessage>(string exchange, List<TMessage> messages, Func<TMessage, string> groupRoutingKeySelector)
    {
        if (messages == null || messages.Count == 0)
            throw new ArgumentNullException(nameof(messages));
        if (groupRoutingKeySelector == null)
            throw new ArgumentNullException(nameof(groupRoutingKeySelector));

        if (!this.producers.TryGetValue(exchange, out var producerInfo))
            throw new Exception($"未知的交换机{exchange}，请先注册集群和生产者");

        var results = new List<Task<TheaResponse>>();
        foreach (var message in messages)
        {
            var routingKeyValue = groupRoutingKeySelector.Invoke(message);
            var routingKey = HashCode.Combine(routingKeyValue) % producerInfo.ConsumerTotalCount;
            var theaMessage = new TheaMessage
            {
                MessageId = ObjectId.NewId(),
                NodeId = this.NodeId,
                ClusterId = producerInfo.ClusterId,
                Exchange = exchange,
                IsGroupMessage = true,
                RoutingKey = routingKey.ToString(),
                Message = message.ToJson(),
                Status = MessageStatus.WaitForReply,
                Waiter = new TaskCompletionSource<TheaResponse>()
            };
            this.messageResults.TryAdd(theaMessage.MessageId, theaMessage);
            this.messageQueue.Enqueue(theaMessage);
            results.Add(theaMessage.Waiter.Task);
        }
        Task.WaitAll(results.ToArray());
        return results.Select(f => f.Result).ToArray();
    }

    public void AddProducer(string clusterId, bool isUseRpc)
    {
        this.producers.TryAdd(clusterId, new ProducerInfo { ClusterId = clusterId, RabbitProducer = new RabbitProducer(clusterId) });
        if (isUseRpc)
        {
            var exchange = clusterId + ".result";
            this.producers.TryAdd(exchange, new ProducerInfo { ClusterId = clusterId, RabbitProducer = new RabbitProducer(clusterId) });
            this.replyConsumers.TryAdd(exchange, new RabbitConsumer(this, this.serviceProvider));
        }
        if (!this.localClusterIds.Contains(clusterId))
            this.localClusterIds.Add(clusterId);
    }
    public void AddConsumer(string clusterId, Func<TheaMessage, Task<TheaResponse>> consumerHandler)
    {
        this.consumerHandlers.TryAdd(clusterId, consumerHandler);
        if (!this.localClusterIds.Contains(clusterId))
            this.localClusterIds.Add(clusterId);
    }
    internal void Next(TheaMessage message)
    {
        switch (message.Status)
        {
            case MessageStatus.WaitForReply:
                if (this.producers.TryGetValue(message.ReplyExchange, out var producer))
                    producer.RabbitProducer.TryPublish(message.ReplyExchange, message.NodeId, message.ToJson());
                break;
            case MessageStatus.SetResult:
                if (this.messageResults.TryRemove(message.MessageId, out var theaMessage))
                {
                    var result = message.Message.JsonTo<TheaResponse>();
                    if (theaMessage.Waiter != null)
                        theaMessage.Waiter.TrySetResult(result);
                }
                break;
        }
    }
    private void EnsureAvailable()
    {
        foreach (var workerConsumer in this.consumers.Values)
            workerConsumer.ForEach(f => f.RabbitConsumer.EnsureAvailable());
        foreach (var replyConsumer in this.replyConsumers.Values)
            replyConsumer.EnsureAvailable();
    }
    private async Task Initialize()
    {
        if (this.NodeId == null)
            throw new Exception("未配置NodeId的环境变量，调试可以使用SetNodeId方法，生产、测试环境可以配置NodeId环境变量！");

        var clusters = await this.repository.GetClusters(this.localClusterIds);
        var bindings = await this.repository.GetBindings(this.localClusterIds);
        var consumers = await this.repository.GetConsumers(this.NodeId);
        foreach (var cluster in clusters)
        {
            bool isRebuilding = true;
            if (!this.clusters.TryGetValue(cluster.ClusterId, out var localClusterInfo))
                this.clusters.TryAdd(cluster.ClusterId, cluster);
            else if (cluster.UpdatedAt > localClusterInfo.UpdatedAt)
                this.clusters.TryUpdate(cluster.ClusterId, cluster, localClusterInfo);
            else isRebuilding = false;

            if (this.producers.TryGetValue(cluster.ClusterId, out var producerInfo))
            {
                var totalCount = bindings.Count(f => f.ClusterId == cluster.ClusterId);
                producerInfo.ConsumerTotalCount = totalCount;
                producerInfo.RabbitProducer.Create(cluster);
            }
            int index = 0;
            RabbitConsumer resultRabbitConsumer = null;
            List<ConsumerInfo> localConsumerInfos = null;

            var clusterBindings = bindings.FindAll(f => f.ClusterId == cluster.ClusterId);
            var clusterConsumers = consumers.FindAll(f => f.ClusterId == cluster.ClusterId);

            //数据库未绑定任何队列，也没有设置消费者，删除本地现有消费者
            if (!cluster.IsEnabled || clusterBindings == null && clusterBindings.Count == 0
                || clusterConsumers == null || clusterConsumers.Count == 0)
            {
                if (this.consumers.TryRemove(cluster.ClusterId, out localConsumerInfos)
                    && localConsumerInfos != null && localConsumerInfos.Count > 0)

                    localConsumerInfos.ForEach(f => f.RabbitConsumer.Shutdown());
                continue;
            }

            if (isRebuilding)
            {
                if (!this.consumers.TryGetValue(cluster.ClusterId, out localConsumerInfos))
                    this.consumers.TryAdd(cluster.ClusterId, localConsumerInfos = new List<ConsumerInfo>());
                clusterConsumers.Sort((x, y) => x.Queue.CompareTo(y.Queue));

                //重建本地消费者
                index = 0; bool isNewConsumer = false;
                foreach (var consumer in clusterConsumers)
                {
                    if (consumer.IsReply) continue;

                    var consumerHandler = this.consumerHandlers[cluster.ClusterId];
                    ConsumerInfo localConsumerInfo = null;
                    if (localConsumerInfos.Count <= index)
                    {
                        localConsumerInfos.Add(localConsumerInfo = new ConsumerInfo
                        {
                            ClusterId = cluster.ClusterId,
                            UpdatedAt = consumer.UpdatedAt,
                            RabbitConsumer = new RabbitConsumer(this, this.serviceProvider, consumerHandler)
                        });
                        isNewConsumer = true;
                    }
                    else
                    {
                        localConsumerInfo = localConsumerInfos[index];
                        isRebuilding = isRebuilding || consumer.UpdatedAt > localConsumerInfo.UpdatedAt;
                    }
                    if (isRebuilding || isNewConsumer || consumer.UpdatedAt > localConsumerInfo.UpdatedAt)
                    {
                        var queueBinding = clusterBindings.Find(f => f.Queue == consumer.Queue);
                        localConsumerInfo.RabbitConsumer.Bind(consumer.ConsumerId, cluster, queueBinding).Start();
                    }
                    index++;
                }
                //删除多余的本地消费者
                if (localConsumerInfos.Count > clusterConsumers.Count)
                {
                    index = 0;
                    while (index > clusterConsumers.Count)
                        localConsumerInfos[index].RabbitConsumer.Shutdown();
                }
                //重建结果队列
                if (this.replyConsumers.TryGetValue(cluster.ClusterId, out resultRabbitConsumer) && isRebuilding)
                    resultRabbitConsumer.Bind($"{cluster.ClusterId}.{this.NodeId}.result", cluster).Start();
            }
        }
    }
}