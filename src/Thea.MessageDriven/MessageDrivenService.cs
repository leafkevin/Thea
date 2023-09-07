using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Thea.Logging;
using Thea.Orm;

namespace Thea.MessageDriven;

class MessageDrivenService : IMessageDriven
{
    private readonly Task task;
    private readonly CancellationTokenSource cancellationSource = new CancellationTokenSource();
    private readonly EventWaitHandle readyToStart = new EventWaitHandle(false, EventResetMode.AutoReset);
    private readonly List<string> localClusterIds = new();
    private readonly ConcurrentQueue<Message> messageQueue = new();
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
    private DateTime lastInitedTime = DateTime.MinValue;
    private DateTime lastUpdateTime = DateTime.MinValue;

    public string HostName { get; set; }
    public string DbKey { get; set; }

    public MessageDrivenService(IServiceProvider serviceProvider)
    {
        this.HostName = Environment.GetEnvironmentVariable("NodeId");
        this.serviceProvider = serviceProvider;
        this.logger = serviceProvider.GetService<ILogger<MessageDrivenService>>();

        this.task = Task.Factory.StartNew(async () =>
        {
            this.readyToStart.WaitOne();

            if (string.IsNullOrEmpty(this.HostName))
                this.HostName = Dns.GetHostName();
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
                    if (DateTime.Now - this.lastInitedTime > TimeSpan.FromHours(1))
                    {
                        await this.Initialize();
                        //确保Consumer是活的
                        this.EnsureAvailable();
                        this.lastInitedTime = DateTime.Now;
                    }
                    if (DateTime.Now - this.lastUpdateTime > TimeSpan.FromSeconds(10))
                    {
                        await this.repository.Update(this.HostName);
                        if (logs.Count > 0)
                        {
                            await this.repository.AddLogs(logs);
                            logs.Clear();
                        }
                        this.lastUpdateTime = DateTime.Now;
                    }
                    if (logs.Count >= 200)
                    {
                        await this.repository.AddLogs(logs);
                        logs.Clear();
                    }
                    if (this.messageQueue.TryDequeue(out var message))
                    {
                        switch (message.Type)
                        {
                            case MessageType.TheaMessage:
                                var theaMessage = message.Body as TheaMessage;
                                if (!this.producers.TryGetValue(theaMessage.Exchange, out var producerInfo))
                                    throw new Exception($"未知的交换机{theaMessage.Exchange}，请先注册集群和生产者");

                                var routingKey = HashCode.Combine(theaMessage.RoutingKey) % producerInfo.ConsumerTotalCount;
                                producerInfo.RabbitProducer.TryPublish(theaMessage.Exchange, routingKey.ToString(), theaMessage.ToJson());
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
        if (this.task != null)
            this.task.Wait();
        this.cancellationSource.Dispose();
    }
    public TheaResponse Request<TMessage>(string exchange, string routingKey, TMessage message)
    {
        var theaMessage = new TheaMessage
        {
            MessageId = ObjectId.NewId(),
            NodeId = this.HostName,
            Exchange = exchange,
            ReplyExchange = exchange + ".result",
            RoutingKey = routingKey,
            Message = message.ToJson(),
            Status = MessageStatus.WaitForReply,
            Waiter = new TaskCompletionSource<TheaResponse>()
        };
        this.messageResults.TryAdd(theaMessage.MessageId, theaMessage);
        this.messageQueue.Enqueue(new Message { Type = MessageType.TheaMessage, Body = theaMessage });
        return theaMessage.Waiter.Task.Result;
    }
    public async Task<TheaResponse> RequestAsync<TMessage>(string exchange, string routingKey, TMessage message)
    {
        var theaMessage = new TheaMessage
        {
            MessageId = ObjectId.NewId(),
            NodeId = this.HostName,
            Exchange = exchange,
            ReplyExchange = exchange + ".result",
            RoutingKey = routingKey,
            Message = message.ToJson(),
            Status = MessageStatus.WaitForReply,
            Waiter = new TaskCompletionSource<TheaResponse>()
        };
        this.messageResults.TryAdd(theaMessage.MessageId, theaMessage);
        this.messageQueue.Enqueue(new Message { Type = MessageType.TheaMessage, Body = theaMessage });
        return await theaMessage.Waiter.Task;
    }
    public void Publish<TMessage>(string exchange, string routingKey, TMessage message)
    {
        if (!this.producers.TryGetValue(exchange, out var producerInfo))
            throw new Exception($"未知的交换机{exchange}，请先注册集群和生产者");
        var theaMessage = new TheaMessage
        {
            MessageId = ObjectId.NewId(),
            NodeId = this.HostName,
            ClusterId = producerInfo.ClusterId,
            Exchange = exchange,
            RoutingKey = routingKey,
            Message = message.ToJson(),
            Status = MessageStatus.None
        };
        this.messageQueue.Enqueue(new Message { Type = MessageType.TheaMessage, Body = theaMessage });
    }
    public Task PublishAsync<TMessage>(string exchange, string routingKey, TMessage message)
    {
        this.Publish(exchange, routingKey, message);
        return Task.CompletedTask;
    }
    public TheaResponse[] Request<TMessage>(string exchange, List<TMessage> messages, Func<TMessage, string> routingKeySelector)
    {
        if (messages == null || messages.Count == 0)
            throw new ArgumentNullException(nameof(messages));
        if (routingKeySelector == null)
            throw new ArgumentNullException(nameof(routingKeySelector));

        if (!this.producers.TryGetValue(exchange, out var producerInfo))
            throw new Exception($"未知的交换机{exchange}，请先注册集群和生产者");

        var results = new List<Task<TheaResponse>>();
        foreach (var message in messages)
        {
            var routingKeyValue = routingKeySelector.Invoke(message);
            var routingKey = HashCode.Combine(routingKeyValue) % producerInfo.ConsumerTotalCount;
            var theaMessage = new TheaMessage
            {
                MessageId = ObjectId.NewId(),
                NodeId = this.HostName,
                ClusterId = producerInfo.ClusterId,
                Exchange = exchange,
                IsGroupMessage = true,
                RoutingKey = routingKey.ToString(),
                Message = message.ToJson(),
                Status = MessageStatus.WaitForReply,
                Waiter = new TaskCompletionSource<TheaResponse>()
            };
            this.messageResults.TryAdd(theaMessage.MessageId, theaMessage);
            this.messageQueue.Enqueue(new Message { Type = MessageType.TheaMessage, Body = theaMessage });
            results.Add(theaMessage.Waiter.Task);
        }
        Task.WaitAll(results.ToArray());
        return results.Select(f => f.Result).ToArray();
    }
    public async Task<TheaResponse[]> RequestAsync<TMessage>(string exchange, List<TMessage> messages, Func<TMessage, string> routingKeySelector)
    {
        if (messages == null || messages.Count == 0)
            throw new ArgumentNullException(nameof(messages));
        if (routingKeySelector == null)
            throw new ArgumentNullException(nameof(routingKeySelector));

        if (!this.producers.TryGetValue(exchange, out var producerInfo))
            throw new Exception($"未知的交换机{exchange}，请先注册集群和生产者");

        var results = new List<Task<TheaResponse>>();
        foreach (var message in messages)
        {
            var routingKeyValue = routingKeySelector.Invoke(message);
            var routingKey = HashCode.Combine(routingKeyValue) % producerInfo.ConsumerTotalCount;
            var theaMessage = new TheaMessage
            {
                MessageId = ObjectId.NewId(),
                NodeId = this.HostName,
                ClusterId = producerInfo.ClusterId,
                Exchange = exchange,
                IsGroupMessage = true,
                RoutingKey = routingKey.ToString(),
                Message = message.ToJson(),
                Status = MessageStatus.WaitForReply,
                Waiter = new TaskCompletionSource<TheaResponse>()
            };
            this.messageResults.TryAdd(theaMessage.MessageId, theaMessage);
            this.messageQueue.Enqueue(new Message { Type = MessageType.TheaMessage, Body = theaMessage });
            results.Add(theaMessage.Waiter.Task);
        }
        return await Task.WhenAll(results);
    }

    public void AddProducer(string clusterId, bool isUseRpc)
    {
        this.producers.TryAdd(clusterId, new ProducerInfo
        {
            ClusterId = clusterId,
            IsUseRpc = isUseRpc,
            RabbitProducer = new RabbitProducer()
        });
        if (isUseRpc)
        {
            var exchange = clusterId + ".result";
            this.replyConsumers.TryAdd(exchange, new RabbitConsumer(this, this.serviceProvider));
        }
        if (!this.localClusterIds.Contains(clusterId))
            this.localClusterIds.Add(clusterId);
    }
    public void AddConsumer(string clusterId, Func<TheaMessage, Task<TheaResponse>> consumerHandler)
    {
        var consumerInfo = new ConsumerInfo
        {
            ClusterId = clusterId,
            ConsumerId = $"{clusterId}.{this.HostName}.worker0",
            RabbitConsumer = new RabbitConsumer(this, this.serviceProvider, consumerHandler)
        };
        this.consumers.TryAdd(clusterId, new List<ConsumerInfo> { consumerInfo });
        this.consumerHandlers.TryAdd(clusterId, consumerHandler);
        if (!this.localClusterIds.Contains(clusterId))
            this.localClusterIds.Add(clusterId);
    }
    public void Next(TheaMessage message)
    {
        switch (message.Status)
        {
            case MessageStatus.WaitForReply:
                if (!this.producers.TryGetValue(message.ReplyExchange, out var producer))
                    this.producers.TryAdd(message.ReplyExchange, new ProducerInfo { ClusterId = message.ClusterId, RabbitProducer = new RabbitProducer() });

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
        (var dbClusters, var dbBindings, var dbConsumers) = await this.repository.GetClusterInfo(this.HostName, this.localClusterIds);
        var registerClusters = new List<Cluster>();
        var registerBindings = new List<Binding>();
        var registerConsumers = new List<Consumer>();
        foreach (var clusterId in this.localClusterIds)
        {
            var now = DateTime.UtcNow;
            var dbClusterInfo = dbClusters.Find(f => f.ClusterId == clusterId);
            if (dbClusterInfo == null)
            {
                registerClusters.Add(dbClusterInfo = new Cluster
                {
                    ClusterId = clusterId,
                    ClusterName = clusterId,
                    BindType = "direct",
                    IsEnabled = true,
                    CreatedAt = now,
                    CreatedBy = this.HostName,
                    UpdatedAt = now,
                    UpdatedBy = this.HostName
                });
            }
            //dbClusterInfo.UpdatedAt = DateTime.MinValue;
            this.clusters.TryAdd(clusterId, dbClusterInfo);
            //手动更改
            if (!dbClusterInfo.IsEnabled) continue;

            var clusterBindings = dbBindings.FindAll(f => f.ClusterId == clusterId);
            var ipAddress = this.GetIpAddress();
            if (clusterBindings == null || clusterBindings.Count == 0)
            {
                var queue = $"{clusterId}.{this.HostName}.queue{dbBindings.Count}";
                registerBindings.Add(new Binding
                {
                    BindingId = $"{clusterId}.{this.HostName}{dbBindings.Count}",
                    ClusterId = clusterId,
                    BindType = "direct",
                    BindingKey = dbBindings.Count.ToString(),
                    Exchange = clusterId,
                    Queue = queue,
                    HostName = this.HostName,
                    IsReply = false,
                    IsEnabled = true,
                    CreatedAt = now,
                    CreatedBy = this.HostName,
                    UpdatedAt = now,
                    UpdatedBy = this.HostName
                });
                registerConsumers.Add(new Consumer
                {
                    ConsumerId = $"{clusterId}.{this.HostName}.worker{dbBindings.Count}",
                    ClusterId = clusterId,
                    HostName = this.HostName,
                    IpAddress = ipAddress,
                    Queue = queue,
                    IsReply = false,
                    IsEnabled = true,
                    CreatedAt = now,
                    CreatedBy = this.HostName,
                    UpdatedAt = now,
                    UpdatedBy = this.HostName
                });
                if (this.replyConsumers.TryGetValue(clusterId + ".result", out _))
                {
                    queue = $"{clusterId}.{this.HostName}.result";
                    registerBindings.Add(new Binding
                    {
                        BindingId = $"{clusterId}.result",
                        ClusterId = clusterId,
                        BindType = "direct",
                        BindingKey = this.HostName,
                        Exchange = $"{clusterId}.result",
                        Queue = queue,
                        HostName = this.HostName,
                        IsReply = true,
                        IsEnabled = true,
                        CreatedAt = now,
                        CreatedBy = this.HostName,
                        UpdatedAt = now,
                        UpdatedBy = this.HostName
                    });
                    registerConsumers.Add(new Consumer
                    {
                        ConsumerId = $"{clusterId}.{this.HostName}.result",
                        ClusterId = clusterId,
                        HostName = this.HostName,
                        IpAddress = ipAddress,
                        Queue = queue,
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
        if (registerConsumers.Count > 0)
            await this.repository.Register(registerConsumers);
    }
    private async Task Initialize()
    {
        (var dbClusters, var dbBindings, var dbConsumers) = await this.repository.GetClusterInfo(this.HostName, this.localClusterIds);
        var registerClusters = new List<Cluster>();
        var registerBindings = new List<Binding>();
        var registerConsumers = new List<Consumer>();
        var localClusterInfos = this.clusters.Values.ToList();
        foreach (var localClusterInfo in localClusterInfos)
        {
            var dbClusterInfo = dbClusters.Find(f => f.ClusterId == localClusterInfo.ClusterId);
            var clusterBindings = dbBindings.FindAll(f => f.ClusterId == localClusterInfo.ClusterId);
            var clusterConsumers = dbConsumers.FindAll(f => f.ClusterId == localClusterInfo.ClusterId);
            var isEmpty = clusterBindings == null || clusterBindings.Count == 0
               || clusterConsumers == null || clusterConsumers.Count == 0;

            //集群信息不存在或是无效，生产者和消费者都不建立
            if (dbClusterInfo == null || !dbClusterInfo.IsEnabled || string.IsNullOrEmpty(dbClusterInfo.Url) || isEmpty)
            {
                //不删除本地创建的对象，以免来回创建浪费性能
                //if (this.producers.TryRemove(localClusterInfo.ClusterId, out var removedProducerInfo))
                //    removedProducerInfo?.RabbitProducer?.Close();

                //if (this.consumers.TryRemove(localClusterInfo.ClusterId, out var removedConsumerInfos))
                //    removedConsumerInfos?.ForEach(f => f.RabbitConsumer?.Shutdown());
                continue;
            }

            var clusterId = localClusterInfo.ClusterId;
            this.clusters[clusterId] = dbClusterInfo;

            RabbitProducer rabbitProducer = null;
            if (this.producers.TryGetValue(clusterId, out var producerInfo))
            {
                var totalCount = dbBindings.Count(f => f.ClusterId == clusterId && !f.IsReply);
                producerInfo.ConsumerTotalCount = totalCount;

                producerInfo.RabbitProducer.Create(localClusterInfo);
                rabbitProducer = producerInfo.RabbitProducer;
            }
            //确保交换机、队列及绑定存在
            //if (clusterBindings.Count > 0)
            //{
            //    if (clusterBindings.Count > 1)
            //        clusterBindings.Sort((x, y) => x.Queue.CompareTo(y.Queue));

            //    bool isCreated = false;
            //    if (rabbitProducer == null)
            //    {
            //        rabbitProducer = new RabbitProducer(1).Create(dbClusterInfo);
            //        isCreated = true;
            //    }
            //    foreach (var dbClusterBinding in clusterBindings)
            //    {
            //        if (dbClusterBinding.IsReply)
            //            rabbitProducer.CreateReplyQueue(clusterId, this.HostName);
            //        else rabbitProducer.CreateWorkerQueue(clusterId, "direct", dbClusterBinding.BindingKey, dbClusterBinding.Queue);
            //    }
            //    if (isCreated) rabbitProducer.Close();
            //}

            if (this.consumers.TryGetValue(clusterId, out var localConsumerInfos))
            {
                var requiredBindings = clusterBindings.FindAll(f => f.HostName == this.HostName);
                requiredBindings.Sort((x, y) => x.Queue.CompareTo(y.Queue));
                for (int i = 0; i < requiredBindings.Count; i++)
                {
                    var dbBinding = requiredBindings[i];
                    ConsumerInfo localConsumerInfo = null;
                    if (i < localConsumerInfos.Count)
                        localConsumerInfo = localConsumerInfos[i];
                    else
                    {
                        localConsumerInfo = new ConsumerInfo
                        {
                            ClusterId = clusterId,
                            ConsumerId = dbBinding.IsReply ? $"{clusterId}.{this.HostName}.result" : $"{clusterId}.{this.HostName}.worker{i}"
                        };
                        if (dbBinding.IsReply)
                            localConsumerInfo.RabbitConsumer = new RabbitConsumer(this, this.serviceProvider);
                        else localConsumerInfo.RabbitConsumer = new RabbitConsumer(this, this.serviceProvider, this.consumerHandlers[clusterId]);
                    }
                    localConsumerInfo.RabbitConsumer.Bind(localConsumerInfo.ConsumerId, dbClusterInfo, dbBinding).Start();
                }

                //删除多余的本地消费者
                var index = localConsumerInfos.Count - 1;
                while (index >= requiredBindings.Count)
                {
                    localConsumerInfos[index].RabbitConsumer.Shutdown();
                    localConsumerInfos.RemoveAt(index);
                    index--;
                }
                //重建结果队列
                if (this.replyConsumers.TryGetValue(localClusterInfo.ClusterId, out var resultRabbitConsumer))
                    resultRabbitConsumer.Bind($"{localClusterInfo.ClusterId}.{this.HostName}.result", localClusterInfo).Start();
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
}