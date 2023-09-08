﻿using Microsoft.Extensions.DependencyInjection;
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
using Thea.Json;
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
    //Key=exchange, cluster.result
    private readonly ConcurrentDictionary<string, RabbitConsumer> replyConsumers = new();
    private readonly ConcurrentDictionary<string, ResultWaiter> messageResults = new();
    private readonly ConcurrentDictionary<string, Func<string, Task<object>>> consumerHandlers = new();
    private readonly IServiceProvider serviceProvider;
    private readonly ILogger<MessageDrivenService> logger;

    internal ClusterRepository repository;
    private DateTime lastInitedTime = DateTime.MinValue;
    private DateTime lastUpdateTime = DateTime.MinValue;

    public string HostName { get; set; }
    public string DbKey { get; set; }

    public MessageDrivenService(IServiceProvider serviceProvider)
    {
        this.HostName = Environment.GetEnvironmentVariable("HostName");
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

                                int routingKey = 0;
                                if (producerInfo.ConsumerTotalCount > 1)
                                    routingKey = HashCode.Combine(theaMessage.RoutingKey) % producerInfo.ConsumerTotalCount;
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

    public void Publish<TMessage>(string exchange, string routingKey, TMessage message)
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
        this.messageQueue.Enqueue(new Message { Type = MessageType.TheaMessage, Body = theaMessage });
    }
    public Task PublishAsync<TMessage>(string exchange, string routingKey, TMessage message)
    {
        this.Publish(exchange, routingKey, message);
        return Task.CompletedTask;
    }
    public void Publish<TMessage>(string exchange, List<TMessage> messages, Func<TMessage, string> routingKeySelector)
    {
        if (!this.producers.TryGetValue(exchange, out var producerInfo))
            throw new Exception($"未知的交换机{exchange}，请先注册集群和生产者");
        if (messages == null || messages.Count == 0)
            throw new ArgumentNullException(nameof(messages));
        messages.ForEach(f =>
        {
            var routingKeyValue = routingKeySelector.Invoke(f);
            var routingKey = HashCode.Combine(routingKeyValue) % producerInfo.ConsumerTotalCount;
            var theaMessage = new TheaMessage
            {
                MessageId = ObjectId.NewId(),
                HostName = this.HostName,
                ClusterId = producerInfo.ClusterId,
                Exchange = exchange,
                RoutingKey = routingKey.ToString(),
                Message = f.ToJson(),
                Status = MessageStatus.None
            };
            this.messageQueue.Enqueue(new Message { Type = MessageType.TheaMessage, Body = theaMessage });
        });
    }
    public Task PublishAsync<TMessage>(string exchange, List<TMessage> messages, Func<TMessage, string> routingKeySelector)
    {
        this.Publish(exchange, messages, routingKeySelector);
        return Task.CompletedTask;
    }
    public TResponse Request<TRequst, TResponse>(string exchange, string routingKey, TRequst message)
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
        this.messageQueue.Enqueue(new Message { Type = MessageType.TheaMessage, Body = theaMessage });
        return (TResponse)resultWaiter.Waiter.Task.Result;
    }
    public async Task<TResponse> RequestAsync<TRequest, TResponse>(string exchange, string routingKey, TRequest message)
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
        this.messageQueue.Enqueue(new Message { Type = MessageType.TheaMessage, Body = theaMessage });
        var result = await resultWaiter.Waiter.Task;
        return (TResponse)result;
    }
    public List<TResponse> Request<TRequst, TResponse>(string exchange, List<TRequst> messages, Func<TRequst, string> routingKeySelector)
    {
        if (!this.producers.TryGetValue(exchange, out var producerInfo))
            throw new Exception($"未知的交换机{exchange}，请先注册集群和生产者");
        if (messages == null || messages.Count == 0)
            throw new ArgumentNullException(nameof(messages));
        if (routingKeySelector == null)
            throw new ArgumentNullException(nameof(routingKeySelector));

        var results = new List<Task<object>>();
        foreach (var message in messages)
        {
            var routingKeyValue = routingKeySelector.Invoke(message);
            var routingKey = HashCode.Combine(routingKeyValue) % producerInfo.ConsumerTotalCount;
            var theaMessage = new TheaMessage
            {
                MessageId = ObjectId.NewId(),
                HostName = this.HostName,
                ClusterId = producerInfo.ClusterId,
                Exchange = exchange,
                RoutingKey = routingKey.ToString(),
                Message = message.ToJson(),
                Status = MessageStatus.WaitForReply
            };
            var resultWaiter = new ResultWaiter { ResponseType = typeof(TResponse), Waiter = new TaskCompletionSource<object>() };
            this.messageResults.TryAdd(theaMessage.MessageId, resultWaiter);
            this.messageQueue.Enqueue(new Message { Type = MessageType.TheaMessage, Body = theaMessage });
            results.Add(resultWaiter.Waiter.Task);
        }
        Task.WaitAll(results.ToArray());
        return results.Select(f => (TResponse)f.Result).ToList();
    }
    public async Task<List<TResponse>> RequestAsync<TRequst, TResponse>(string exchange, List<TRequst> messages, Func<TRequst, string> routingKeySelector)
    {
        if (!this.producers.TryGetValue(exchange, out var producerInfo))
            throw new Exception($"未知的交换机{exchange}，请先注册集群和生产者");
        if (messages == null || messages.Count == 0)
            throw new ArgumentNullException(nameof(messages));
        if (routingKeySelector == null)
            throw new ArgumentNullException(nameof(routingKeySelector));

        var taskResults = new List<Task<object>>();
        foreach (var message in messages)
        {
            var routingKeyValue = routingKeySelector.Invoke(message);
            var routingKey = HashCode.Combine(routingKeyValue) % producerInfo.ConsumerTotalCount;
            var theaMessage = new TheaMessage
            {
                MessageId = ObjectId.NewId(),
                HostName = this.HostName,
                ClusterId = producerInfo.ClusterId,
                Exchange = exchange,
                RoutingKey = routingKey.ToString(),
                Message = message.ToJson(),
                Status = MessageStatus.WaitForReply
            };
            var resultWaiter = new ResultWaiter { ResponseType = typeof(TResponse), Waiter = new TaskCompletionSource<object>() };
            this.messageResults.TryAdd(theaMessage.MessageId, resultWaiter);
            this.messageQueue.Enqueue(new Message { Type = MessageType.TheaMessage, Body = theaMessage });
            taskResults.Add(resultWaiter.Waiter.Task);
        }
        var results = await Task.WhenAll(taskResults);
        return results.Cast<TResponse>().ToList();
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
    public void AddConsumer(string clusterId, object target, Type parametersType, ObjectMethodExecutor methodExecutor)
    {
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
            RabbitConsumer = new RabbitConsumer(this, this.serviceProvider, consumerHandler)
        };
        this.consumers.TryAdd(clusterId, new List<ConsumerInfo> { consumerInfo });
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

                producer.RabbitProducer.TryPublish(message.ReplyExchange, message.HostName, message.ToJson());
                Console.WriteLine($"WaitForReply Completed: {message.MessageId}");
                break;
            case MessageStatus.SetResult:
                if (this.messageResults.TryRemove(message.MessageId, out var resultWaiter))
                {
                    var result = TheaJsonSerializer.Deserialize(message.Message, resultWaiter.ResponseType);
                    if (resultWaiter.Waiter != null)
                        resultWaiter.Waiter.TrySetResult(result);
                }
                Console.WriteLine($"TrySetResult Completed: {message.MessageId}");
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

            if (this.producers.TryGetValue(clusterId, out var producerInfo))
            {
                var totalCount = dbBindings.Count(f => f.ClusterId == clusterId && !f.IsReply);
                producerInfo.ConsumerTotalCount = totalCount;
                producerInfo.RabbitProducer.Create(localClusterInfo);
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
                var requiredBindings = clusterBindings.FindAll(f => f.HostName == this.HostName && !f.IsReply);
                //localConsumerInfos.Sort((x,y)=>x.)
                requiredBindings.Sort((x, y) => int.Parse(x.BindingKey).CompareTo(int.Parse(y.BindingKey)));
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
                            RoutingKey = dbBinding.BindingKey,
                            ConsumerId = dbBinding.IsReply ? $"{clusterId}.{this.HostName}.result" : $"{clusterId}.{this.HostName}.worker{i}"
                        };
                        if (dbBinding.IsReply)
                            localConsumerInfo.RabbitConsumer = new RabbitConsumer(this, this.serviceProvider);
                        else localConsumerInfo.RabbitConsumer = new RabbitConsumer(this, this.serviceProvider, this.consumerHandlers[clusterId]);
                        localConsumerInfos.Add(localConsumerInfo);
                    }
                    localConsumerInfo.RabbitConsumer.Build(localConsumerInfo.ConsumerId, dbClusterInfo, dbBinding);
                }
                if (this.replyConsumers.TryGetValue(localClusterInfo.ClusterId + ".result", out var resultRabbitConsumer))
                {
                    var replyBinding = clusterBindings.Find(f => f.HostName == this.HostName && f.IsReply);
                    resultRabbitConsumer.Build($"{localClusterInfo.ClusterId}.{this.HostName}.result", localClusterInfo, replyBinding);
                }

                //删除多余的本地消费者
                var index = localConsumerInfos.Count - 1;
                while (index >= requiredBindings.Count)
                {
                    localConsumerInfos[index].RabbitConsumer.Shutdown();
                    localConsumerInfos.RemoveAt(index);
                    index--;
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
}