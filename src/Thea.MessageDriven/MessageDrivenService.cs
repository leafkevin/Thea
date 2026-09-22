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

class MessageDrivenService : IMessageDriven, IHostedService
{
    private Task task;
    private CancellationTokenSource stopTokenSource;
    private readonly SemaphoreSlim shutdownLock = new(1, 1);
    private readonly SemaphoreSlim topologyLock = new(1, 1);
    private readonly TaskCompletionSource<bool> readyToStart = new(TaskCreationOptions.RunContinuationsAsynchronously);
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
    private readonly bool isAllowCreateQueue = false;
    private readonly bool isAllowCreateExchange = false;
    private readonly bool isAllowCreateBinding = false;
    private bool isRpcConsumer = false;

    private List<Binding> bindings = new();
    private List<Queue> queues = new();
    private List<ExchangeTransfer> exchangeTransfers = new();
    private Dictionary<string, Binding> statefulBindings;

    private RabbitConsumer heartbeatConsumer;
    private RabbitConsumer rpcConsumer;
    private string lastNodeIds = null;
    private readonly int sacCount = 2;

    private readonly Dictionary<string, Dictionary<string, MethodInfo>> consumerHandlers = new();
    private readonly IServiceProvider serviceProvider;
    private readonly ILogger<MessageDrivenService> logger;
    private IMessageDrivenRepository repository;
    private DateTime lastHeartbeatTime = DateTime.MinValue;
    private DateTime lastInitedTime = DateTime.MinValue;
    private DateTime lastClearRpcTime = DateTime.MinValue;
    private DateTime lastLoggedTime = DateTime.MinValue;
    private int appState;

    internal readonly TimeSpan heartbeatCycle;
    internal RabbitProducer rabbitProducer;
    internal List<AmqpTcpEndpoint> tcpEndPoints;
    public string AppId { get; private set; }
    public string ServiceId { get; private set; }
    internal bool IsEnabled { get; private set; }

    public MessageDrivenService(IServiceProvider serviceProvider)
    {
        this.ServiceId = ObjectId.NewId();
        this.serviceProvider = serviceProvider;
        this.logger = serviceProvider.GetService<ILogger<MessageDrivenService>>();
        var configuration = serviceProvider.GetService<IConfiguration>();
        this.AppId = configuration.GetValue<string>("AppId");
        this.IsEnabled = configuration.GetValue("MessageDriven:IsEnabled", true);
        if (!this.IsEnabled) return;

        var endPoints = configuration.GetSection("MessageDriven:EndPoints").Get<string[]>();
        if (endPoints == null || endPoints.Length == 0)
            throw new Exception("未设置MessageDriven:EndPoints，无法初始化MessageDrivenService对象");
        this.tcpEndPoints = endPoints.Select(f => AmqpTcpEndpoint.Parse(f)).ToList();
        this.isAllowCreateQueue = configuration.GetValue("MessageDriven:IsAllowCreateQueue", true);
        this.isAllowCreateExchange = configuration.GetValue("MessageDriven:IsAllowCreateExchange", true);
        this.isAllowCreateBinding = configuration.GetValue("MessageDriven:IsAllowCreateBinding", true);
        this.heartbeatCycle = TimeSpan.FromSeconds(configuration.GetValue("MessageDriven:Heartbeat", 10));

        if (string.IsNullOrEmpty(this.AppId))
        {
            this.logger.LogTagError("MessageDriven", "未设置AppId，无法初始化MessageDrivenService对象");
            throw new Exception("未设置AppId，无法初始化MessageDrivenService对象");
        }
        this.sacCount = configuration.GetValue("MessageDriven:SacCount", 2);
    }
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!this.IsEnabled)
        {
            this.logger.LogWarning("MessageDriven is disabled.");
            return;
        }
        try
        {
            await this.readyToStart.Task.WaitAsync(cancellationToken);
            Console.WriteLine($"MessageDriven, ServiceId: {this.ServiceId} is started！");

            this.stopTokenSource = new CancellationTokenSource();
            this.heartbeats[this.ServiceId] = DateTime.UtcNow;
            await this.Register();
            Console.WriteLine("Consumers registering is completed！");
            //先不启动业务消费者
            this.lastInitedTime = DateTime.UtcNow;
            //先启动后台任务，只处理心跳消息和业务消息发送，业务消息会在队列中堆积，等待一个心跳后再启动业务消费者
            this.task = this.ExecuteAsync(this.stopTokenSource.Token);

            //确保在启动业务消费者前，所有pod都收到心跳消息彼此感知到，防止有遗漏pod下次启动时有大量消费者漂移
            for (int i = 0; i < 3; i++)
            {
                await this.rabbitProducer.PublishAsync(Consts.HeartbeatExchange, this.AppId, new BasicProperties
                {
                    Persistent = false,
                    Type = Consts.Heartbeat,
                    DeliveryMode = DeliveryModes.Transient,
                    AppId = this.AppId,
                    MessageId = ObjectId.NewId()
                }, this.ServiceId);
                await Task.Delay(this.heartbeatCycle / 3, cancellationToken);
            }

            //启动业务消费者
            await this.topologyLock.WaitAsync(cancellationToken);
            try
            {
                await this.StartConsumersAsync(true);
                this.lastInitedTime = DateTime.Now;
            }
            finally
            {
                this.topologyLock.Release();
            }
            Interlocked.Exchange(ref this.appState, 1);
            Console.WriteLine("Consumers starting is completed！");

            if (this.task.IsCompleted)
                await this.task;
        }
        catch (Exception ex)
        {
            Interlocked.Exchange(ref this.appState, 2);
            try
            {
                await this.ShutdownConsumersAsync();
                await this.CleanupAsync();
            }
            catch (Exception cleanupException)
            {
                this.logger.LogTagError("MessageDriven", cleanupException, "启动失败后的资源清理异常");
            }
            this.logger.LogTagError("MessageDriven", ex, "MessageDrivenService启动失败");
            Console.WriteLine($"MessageDriven, Starting is failled, {ex}");
            throw;
        }
    }
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await this.shutdownLock.WaitAsync();
        try
        {
            if (Interlocked.Exchange(ref this.appState, 2) == 2)
                return;
            await this.topologyLock.WaitAsync();
            try
            {
                await this.ShutdownConsumersAsync();
            }
            finally
            {
                this.topologyLock.Release();
            }
            this.channel.Writer.TryComplete();
            if (this.task != null)
            {
                this.stopTokenSource?.Cancel();
                //等待后台循环任务结束，即：cancellationToken已取消等待channel排空
                try { await this.task.WaitAsync(cancellationToken); }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    this.logger.LogTagError("MessageDriven", $"等待消息后台任务退出超时，将强制关闭RabbitMQ资源");
                    Console.WriteLine($"等待消息后台任务退出超时，将强制关闭RabbitProducer");
                }
                catch (Exception ex)
                {
                    this.logger.LogTagError("MessageDriven", ex, "消息后台任务异常退出");
                    Console.WriteLine($"等待消息后台任务退出，发生异常，将强制关闭RabbitProducer");
                }
            }
        }
        finally
        {
            await this.CleanupAsync();
            this.shutdownLock.Release();
        }
    }
    public async Task PublishAsync<TMessage>(string exchange, string routingKey, TMessage message, CancellationToken cancellationToken = default)
    {
        this.EnsureAvailable();
        if (message == null)
            throw new ArgumentNullException(nameof(message));
        var traceId = string.Empty;
        if (ScopeLogger.TryGetScope(out var scopeState))
            traceId = scopeState.TraceId;
        var theaMessage = new Message
        {
            MessageId = ObjectId.NewId(),
            Type = Consts.UserMessage,
            TraceId = traceId,
            Exchange = exchange,
            RoutingKey = routingKey,
            Body = message,
            Waiter = new()
        };
        await this.channel.Writer.WriteAsync(theaMessage, cancellationToken);
        await theaMessage.Waiter.Task;
    }
    public async Task PublishRpcAsync<TRequest>(string replyToQueue, string messageId, string exchange, string routingKey, TRequest request, CancellationToken cancellationToken = default)
    {
        this.EnsureAvailable();
        if (request == null)
            throw new ArgumentNullException(nameof(request));

        if (string.IsNullOrEmpty(replyToQueue))
            throw new ArgumentNullException(nameof(replyToQueue));
        if (string.IsNullOrEmpty(messageId))
            throw new ArgumentNullException(nameof(messageId));

        var traceId = string.Empty;
        if (ScopeLogger.TryGetScope(out var scopeState))
            traceId = scopeState.TraceId;
        var theaMessage = new Message
        {
            MessageId = messageId,
            ReplyTo = replyToQueue,
            Type = Consts.RpcMessage,
            TraceId = traceId,
            Exchange = exchange,
            RoutingKey = routingKey,
            Body = request,
            Waiter = new()
        };
        await this.channel.Writer.WriteAsync(theaMessage, cancellationToken);
        await theaMessage.Waiter.Task;
    }
    public async Task<TResponse> RequestAsync<TRequest, TResponse>(string exchange, string routingKey, TRequest request, int timeoutSeconds = 30, CancellationToken cancellationToken = default)
    {
        this.EnsureAvailable();
        if (!this.isRpcConsumer)
        {
            var errMessage = $"未配置RPC消费者，请使用方法：UseRpcConsumer()配置RPC消费者";
            this.logger.LogTagError("MessageDriven", errMessage);
            throw new Exception(errMessage);
        }
        if (request == null)
            throw new ArgumentNullException(nameof(request));

        var traceId = string.Empty;
        if (ScopeLogger.TryGetScope(out var scopeState))
            traceId = scopeState.TraceId;
        var theaMessage = new Message
        {
            MessageId = ObjectId.NewId(),
            ReplyTo = $"rpc.result.{this.ServiceId}",
            Type = Consts.RpcMessage,
            TraceId = traceId,
            Exchange = exchange,
            RoutingKey = routingKey,
            Body = request,
            Waiter = new()
        };
        var exMessage = $"RPC请求超时, 耗时{timeoutSeconds}s, message: {request.ToJson()}, exchange: {exchange}, routingKey: {routingKey}";
        var rpcWaiter = new RpcWaiter { MessageId = theaMessage.MessageId, TimeoutSeconds = timeoutSeconds, TimeoutMessage = exMessage };
        this.rpcWaiters.TryAdd(theaMessage.MessageId, rpcWaiter);
        await this.channel.Writer.WriteAsync(theaMessage, cancellationToken);
        await theaMessage.Waiter.Task;
        //Console.WriteLine($"RpcMessage,Request Message, MessageId: {theaMessage.MessageId}, From:{this.ServiceId}, DateTime: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        var rpcMessage = await rpcWaiter.Waiter.WaitAsync(TimeSpan.FromSeconds(timeoutSeconds), exMessage, cancellationToken);
        this.rpcWaiters.TryRemove(theaMessage.MessageId, out _);
        if (rpcMessage.Type == Consts.RpcFailure)
            throw new Exception(rpcMessage.Body.ToString());
        return rpcMessage.Body.JsonTo<TResponse>();
    }
    public async Task ScheduleAsync<TMessage>(string exchange, string routingKey, TMessage message, DateTime enqueueTimeUtc, CancellationToken cancellationToken = default)
    {
        this.EnsureAvailable();
        if (enqueueTimeUtc < DateTime.UtcNow)
            throw new Exception($"入队时间晚于现在时间，只能选择未来时间");
        if (message == null)
            throw new ArgumentNullException(nameof(message));

        var traceId = string.Empty;
        if (ScopeLogger.TryGetScope(out var scopeState))
            traceId = scopeState.TraceId;
        var theaMessage = new Message
        {
            MessageId = ObjectId.NewId(),
            Type = Consts.UserMessage,
            TraceId = traceId,
            Exchange = exchange,
            RoutingKey = routingKey,
            ScheduleTimeUtc = enqueueTimeUtc,
            Body = message,
            Waiter = new()
        };
        await this.channel.Writer.WriteAsync(theaMessage, cancellationToken);
        await theaMessage.Waiter.Task;
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
        var myQueue = this.queues.Find(f => f.QueueId == queue);
        if (myQueue == null)
        {
            this.queues.Add(new Queue
            {
                QueueId = queue,
                AppId = this.AppId,
                IsQuorumQueue = isQuorumQueue,
                IsStateful = true,
                IsSingleActiveConsumer = isSingleActiveConsumer,
                PrefetchCount = 250,
                WorkloadTotal = 2,
                IsLogEnabled = false
            });
        }
        else
        {
            //队列绑定了exchange交换机的有状态队列，同时又作为其他交换机的无状态队列订阅，以有状态队列配置为主
            myQueue.IsQuorumQueue = isQuorumQueue;
            myQueue.IsSingleActiveConsumer = isSingleActiveConsumer;
            myQueue.PrefetchCount = 250;
            myQueue.WorkloadTotal = 2;
        }
        if (!this.bindings.Exists(f => f.ExchangeId == exchange && f.QueueId == queue))
        {
            this.bindings.Add(new Binding
            {
                ExchangeId = exchange,
                QueueId = queue,
                BindType = Consts.TopicBindingType,
                IsDelay = false
            });
        }
    }
    public void UseSubscriber(string queue, MethodInfo methodInfo, bool isQuorumQueue = true)
    {
        if (methodInfo == null)
            throw new ArgumentNullException(nameof(methodInfo));

        this.hasConsumer = true;
        if (!this.consumerHandlers.TryGetValue(queue, out var exchangeHandlers))
            this.consumerHandlers.TryAdd(queue, exchangeHandlers = new());
        exchangeHandlers.TryAdd(string.Empty, methodInfo);

        //交换机与队列的绑定，是在外部系统完成的，广播模式的交换机或是默认交换机
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
        //无状态队列，允许多个交换机绑定到同一个队列，不同的队列不同的消费者，根据不同的routingKey路由到不同的队列中
        var bindingType = isDelay ? Consts.TopicBindingType : Consts.DelayBindingType;
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
                BindingKey = Consts.FanoutRoutingKey,
                IsDelay = isDelay
            });
        }
    }
    public void UseRpcConsumer() => this.isRpcConsumer = true;
    public async Task Change(string queue, int workloadTotal, int? prefetchCount = null, bool? isLogEnabled = null)
    {
        this.EnsureAvailable();
        var myQueue = this.queues.Find(f => f.QueueId == queue);
        if (myQueue == null) return;
        if (prefetchCount.HasValue)
            myQueue.PrefetchCount = prefetchCount.Value;
        if (isLogEnabled.HasValue)
            myQueue.IsLogEnabled = isLogEnabled.Value;

        var oldWorkloadTotal = myQueue.WorkloadTotal;
        if (myQueue.IsStateful && workloadTotal > oldWorkloadTotal)
        {
            var myBindings = this.bindings.FindAll(f => f.QueueId == queue);
            for (int i = oldWorkloadTotal; i < workloadTotal; i++)
            {
                var queueName = $"{queue}.{i}";
                if (this.isAllowCreateQueue)
                    await this.rabbitProducer.CreateQueue(queueName, myQueue.IsQuorumQueue, myQueue.IsSingleActiveConsumer, false);

                if (!this.isAllowCreateBinding) continue;
                foreach (var myBinding in myBindings)
                {
                    await this.rabbitProducer.BindQueue(myBinding.ExchangeId, queueName, i.ToString());
                }
            }
        }
        myQueue.WorkloadTotal = workloadTotal;
        await this.repository.Change(myQueue);
    }
    public async Task RemoveQueue(string queue, int minIndex, int maxIndex)
    {
        this.EnsureAvailable();
        for (int i = minIndex; i <= maxIndex; i++)
        {
            var queueName = $"{queue}.{i}";
            await this.rabbitProducer.RemoveQueue(queueName);
        }
    }
    internal void Start() => this.readyToStart.TrySetResult(true);
    internal void UseRepository(IMessageDrivenRepository repository) => this.repository = repository;
    internal async Task ProcessMessage(Message message)
    {
        switch (message.Type)
        {
            case Consts.Heartbeat:
                var serviceId = message.Body as string;
                this.heartbeats[serviceId] = DateTime.UtcNow;
                break;
            case Consts.RpcResponse:
            case Consts.RpcFailure:
                if (this.rpcWaiters.TryRemove(message.MessageId, out var rpcWaiter))
                    rpcWaiter.Waiter.TrySetResult(message);
                break;
            default:
                //只要转发消息进入channel中排队
                await this.channel.Writer.WriteAsync(message);
                break;
        }
    }
    private async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var logs = new List<ExecLog>();
        while (!stoppingToken.IsCancellationRequested || this.channel.Reader.TryPeek(out _))
        {
            Message message = null;
            BasicProperties properties = null;
            try
            {
                await this.SendHeartbeat();
                for (int i = 0; i < 10; i++)
                {
                    if (!this.channel.Reader.TryRead(out message))
                        break;
                    await this.SendHeartbeat();
                    try
                    {
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
                                //通常都是json格式，只有rpc消费者返回的结果，可能是json，也可能是字符串响应
                                var jsonMessage = message.IsJsonMessage ? message.Body.ToString() : message.Body.ToJson();
                                if (this.statefulBindings.TryGetValue(message.Exchange, out var myBinding))
                                {
                                    if (myBinding.AppId == this.AppId)
                                    {
                                        //如果存在有状态队列，根据消息的RoutingKey进行一致性哈希，路由到对应的队列中
                                        var routingKey = JumpConsistentHash.GetBucket(message.RoutingKey, myBinding.WorkloadTotal);
                                        await this.rabbitProducer.PublishAsync(message.Exchange, routingKey.ToString(), properties, jsonMessage);
                                    }
                                    else
                                    {
                                        //如果队列消费者是其他应用的，发到转发队列中
                                        properties.Headers.Add("Exchange", message.Exchange);
                                        properties.Headers.Add("RoutingKey", message.RoutingKey);
                                        var transferQueue = $"{Consts.TransferExchange}.{myBinding.AppId}";
                                        await this.rabbitProducer.PublishAsync(Consts.DefaultExchange, transferQueue, properties, jsonMessage);
                                    }
                                }
                                //如果是无状态队列的消息，直接发送交换机
                                else await this.rabbitProducer.PublishAsync(message.Exchange, message.RoutingKey, properties, jsonMessage);
                                message.Waiter?.TrySetResult(true);
                                break;
                            case Consts.Logs:
                                if (message.Body is ExecLog execLog)
                                    logs.Add(execLog);
                                const int MaxLogCount = 5000;
                                if (logs.Count > MaxLogCount)
                                {
                                    logs.RemoveRange(0, 1000);
                                    Console.WriteLine("内部日志已满5000条，现已删除1000条");
                                }
                                break;
                        }
                    }
                    catch (Exception ex)
                    {
                        if (message.Type == Consts.UserMessage || message.Type == Consts.RpcMessage)
                        {
                            message.RetryTimes++;
                            if (message.RetryTimes > 3)
                            {
                                var exception = ex.InnerException ?? ex;
                                var errMessage = $"发送消息失败，RetryTimes: {message.RetryTimes - 1}，Message: {message.ToJson()}";
                                this.logger.LogTagError("MessageDriven", exception, errMessage);
                                message.Waiter?.TrySetException(new Exception(errMessage, exception));
                            }
                            else if (!this.channel.Writer.TryWrite(message))
                            {
                                var retryException = new InvalidOperationException("消息发送失败且内部Channel已关闭，无法重新入队。", ex);
                                message.Waiter?.TrySetException(retryException);
                                this.logger.LogTagError("MessageDriven", retryException, $"Message: {message.ToJson()}");
                            }
                        }
                    }
                }
                if (Volatile.Read(ref this.appState) == 1)
                {
                    if (DateTime.UtcNow - this.lastInitedTime >= this.heartbeatCycle * 2)
                    {
                        await this.topologyLock.WaitAsync(stoppingToken);
                        try
                        {
                            if (Volatile.Read(ref this.appState) == 1)
                            {
                                await this.StartConsumersAsync(false);
                                this.lastInitedTime = DateTime.UtcNow;
                            }
                        }
                        finally
                        {
                            this.topologyLock.Release();
                        }
                    }
                    if (!this.rpcWaiters.IsEmpty && DateTime.UtcNow.Subtract(this.lastClearRpcTime).TotalSeconds > 5)
                    {
                        var messageIds = this.rpcWaiters.Keys.ToList();
                        foreach (var messageId in messageIds)
                        {
                            await this.SendHeartbeat();
                            if (!this.rpcWaiters.TryGetValue(messageId, out var rpcWaiter))
                                continue;
                            var timeElapsed = DateTime.UtcNow.Subtract(rpcWaiter.CreatedAt).TotalSeconds;
                            if (timeElapsed < rpcWaiter.TimeoutSeconds)
                                continue;
                            this.rpcWaiters.TryRemove(messageId, out _);
                            //只结束超时的RPC请求，不做清理工作，清理工作在Request方法中处理
                            rpcWaiter.Waiter.TrySetException(new TimeoutException($"RPC请求超时, 耗时{timeElapsed}s"));
                        }
                        this.lastClearRpcTime = DateTime.UtcNow;
                    }
                    if ((DateTime.UtcNow - this.lastLoggedTime > TimeSpan.FromSeconds(10) && logs.Count > 0) || logs.Count >= 100)
                    {
                        var myLogs = logs.Take(100).ToList();
                        await this.repository.WriteLogs(myLogs);
                        logs.RemoveRange(0, myLogs.Count);
                        this.lastLoggedTime = DateTime.UtcNow;
                    }
                    if (!this.channel.Reader.TryPeek(out _) || DateTime.UtcNow - this.lastHeartbeatTime < this.heartbeatCycle)
                        await Task.Delay(1);
                }
                else await Task.Delay(1);
            }
            catch (Exception ex)
            {
                var exception = ex.InnerException ?? ex;
                this.logger.LogTagError("MessageDriven", exception, $"ExecuteAsync is failled");
            }
        }
        if (logs.Count > 0 && this.repository != null)
        {
            try
            {
                await this.repository.WriteLogs(logs);
            }
            catch (Exception ex)
            {
                this.logger.LogTagError("MessageDriven", ex, "退出时写入消息日志失败");
            }
        }
    }
    private async Task Register()
    {
        await this.repository.Register(this.queues, this.bindings);

        //捞取数据库或是配置中心的集群信息
        (var dbQueues, var dbBindings) = await this.repository.GetSettings(false);

        //一个交换机对应一块业务
        //一个交换机顶多绑定一个有状态队列，但可以同时绑定多个无状态队列
        //允许多个交换机绑定到同一个有状态队列
        var exchangeQueues = dbBindings.Where(f => f.IsStateful)
            .GroupBy(f => f.ExchangeId).ToDictionary(f => f.Key, f => f.Select(t => t.QueueId).ToList())
            .Where(f => f.Value.Count > 1).ToList();
        if (exchangeQueues.Count > 0)
        {
            foreach (var exchangeQueue in exchangeQueues)
                throw new Exception($"与交换机{exchangeQueue.Key}绑定的有状态队列只能一个，目前绑定的有状态队列[{string.Join(',', exchangeQueue.Value)}]");
        }
        this.statefulBindings = dbBindings.Where(f => f.IsStateful).ToDictionary(f => f.ExchangeId, f => f);

        //创建交换机和队列及绑定
        this.rabbitProducer = await RabbitProducer.CreateAsync(this, this.serviceProvider);
        //创建RPC消费者
        if (this.isRpcConsumer)
        {
            if (!this.isAllowCreateQueue)
                throw new Exception("未配置允许创建队列，无法创建RPC结果队列，请检查配置项：IsAllowCreateQueue");

            var rpcQueueName = $"{Consts.RpcExchange}.{this.AppId}.{this.ServiceId}";
            this.rpcConsumer = new RabbitConsumer(rpcQueueName, rpcQueueName, this, this.serviceProvider, QueueType.RpcResult);
            await this.rpcConsumer.StartAsync();
        }
        //创建队列和绑定，需要捞取数据库或是配置中心的最新信息
        //包含所有应用的队列和绑定信息，交换机可以多个应用公用，每个应用有自己的队列及处理程序
        if (!this.hasConsumer)
        {
            this.queues = dbQueues;
            this.bindings = dbBindings;
            return;
        }

        //创建交换机
        if (this.isAllowCreateExchange)
        {
            //工作交换机
            var myBindings = this.bindings.Select(f => new { f.ExchangeId, f.BindType, f.IsDelay }).Distinct().ToList();
            foreach (var myBinding in myBindings)
                await this.rabbitProducer.CreateExchange(myBinding.ExchangeId, myBinding.BindType, myBinding.IsDelay);
            //心跳交换机
            await this.rabbitProducer.CreateExchange(Consts.HeartbeatExchange, Consts.TopicBindingType);
        }

        //创建队列
        string queueName = null;
        if (this.isAllowCreateQueue)
        {
            //创建工作负载队列            
            foreach (var myQueue in this.queues)
            {
                var dbQueue = dbQueues.Find(f => f.QueueId == myQueue.QueueId);
                if (dbQueue != null)
                {
                    //如果数据库中存在配置，就使用数据库的配置
                    myQueue.WorkloadTotal = dbQueue.WorkloadTotal;
                    myQueue.IsLogEnabled = dbQueue.IsLogEnabled;
                }
                if (myQueue.IsStateful)
                {
                    for (int i = 0; i < myQueue.WorkloadTotal; i++)
                    {
                        queueName = $"{myQueue.QueueId}.{i}";
                        await this.rabbitProducer.CreateQueue(queueName, myQueue.IsQuorumQueue, myQueue.IsSingleActiveConsumer, false);
                    }
                }
                else await this.rabbitProducer.CreateQueue(myQueue.QueueId, myQueue.IsQuorumQueue, false, false);
            }

            //创建转发队列
            if (this.queues.Exists(f => f.IsStateful))
            {
                //转发队列使用SAC消费者
                queueName = $"{Consts.TransferExchange}.{this.AppId}";
                await this.rabbitProducer.CreateQueue(queueName, true, true, false);
            }

            //创建独占心跳队列和消费者
            queueName = $"{Consts.HeartbeatExchange}.{this.AppId}.{this.ServiceId}";
            this.heartbeatConsumer = new RabbitConsumer(queueName, queueName, this, this.serviceProvider, QueueType.Heartbeat);
            await this.heartbeatConsumer.StartAsync();
        }

        //创建队列绑定
        if (this.isAllowCreateBinding)
        {
            //创建工作负载队列绑定
            foreach (var myBinding in this.bindings)
            {
                var myQueue = this.queues.Find(f => f.QueueId == myBinding.QueueId);
                if (myQueue.IsStateful)
                {
                    for (int i = 0; i < myQueue.WorkloadTotal; i++)
                    {
                        queueName = $"{myQueue.QueueId}.{i}";
                        await this.rabbitProducer.BindQueue(myBinding.ExchangeId, queueName, i.ToString());
                    }
                }
                else await this.rabbitProducer.BindQueue(myBinding.ExchangeId, myBinding.QueueId, Consts.FanoutRoutingKey);
            }
        }
    }
    private async Task StartConsumersAsync(bool isFirst)
    {
        await this.SendHeartbeat();
        (var dbQueues, var dbBindings) = await this.repository.GetSettings();
        this.statefulBindings = dbBindings.Where(f => f.IsStateful).ToDictionary(f => f.ExchangeId, f => f);
        if (!this.hasConsumer) return;

        var timeout = isFirst ? this.heartbeatCycle : this.heartbeatCycle * 2;
        var removedKeys = this.heartbeats
            .Where(f => f.Key != this.ServiceId && DateTime.UtcNow.Subtract(f.Value) > timeout)
            .Select(f => f.Key).ToList();
        if (removedKeys.Count > 0)
            removedKeys.ForEach(f => this.heartbeats.TryRemove(f, out _));

        //启动消费者
        var nodeIds = this.heartbeats.Keys.ToList();
        nodeIds.Sort((x, y) => x.CompareTo(y));
        var currentNodeIds = string.Join(",", nodeIds);
        if (currentNodeIds != this.lastNodeIds)
            Console.WriteLine($"可用节点：{currentNodeIds}");

        int index = 0, nodeCount = nodeIds.Count;
        List<RabbitConsumer> rabbitConsumers = null;
        var localConsumerIds = new Dictionary<string, List<string>>();
        var changedQueues = new Dictionary<string, int>();
        var allQueueNames = new HashSet<string>();

        //先创建有状态队列消费者(包括SAC和非SAC消费者)
        var myQueues = this.queues.Where(f => f.AppId == this.AppId && f.IsStateful).OrderBy(f => f.QueueId).ToList();
        foreach (var lastQueue in myQueues)
        {
            var lastWorkloadTotal = lastQueue.WorkloadTotal;
            var dbQueue = dbQueues.Find(f => f.QueueId == lastQueue.QueueId);
            lastQueue.WorkloadTotal = lastWorkloadTotal;
            lastQueue.IsLogEnabled = dbQueue.IsLogEnabled;
            if (lastWorkloadTotal >= dbQueue.WorkloadTotal)
                continue;

            var myBindings = dbBindings.FindAll(f => f.QueueId == lastQueue.QueueId);
            for (int i = lastWorkloadTotal; i < dbQueue.WorkloadTotal; i++)
            {
                var queueName = $"{dbQueue.QueueId}.{i}";
                if (this.isAllowCreateQueue)
                    await this.rabbitProducer.CreateQueue(queueName, dbQueue.IsQuorumQueue, dbQueue.IsSingleActiveConsumer, false);
                if (this.isAllowCreateBinding)
                {
                    foreach (var myBinding in myBindings)
                        await this.rabbitProducer.BindQueue(myBinding.ExchangeId, queueName, i.ToString());
                }
            }
        }

        //先创建有状态队列消费者
        foreach (var myQueue in myQueues)
        {
            for (int i = 0; i < myQueue.WorkloadTotal; i++)
            {
                var queueName = $"{myQueue.QueueId}.{i}";
                allQueueNames.Add(queueName);
                var consumerId = $"{queueName}.{this.ServiceId}.0";
                await this.CreateConsumer(index, myQueue, queueName, consumerId, nodeIds, localConsumerIds);
                index++;
            }
        }
        var statefullQueues = myQueues;

        //创建无状态队列消费者
        myQueues = dbQueues.Where(f => f.AppId == this.AppId && !f.IsStateful).OrderBy(f => f.QueueId).ToList();
        foreach (var myQueue in myQueues)
        {
            var queueName = myQueue.QueueId;
            allQueueNames.Add(queueName);
            for (int i = 0; i < myQueue.WorkloadTotal; i++)
            {
                var consumerId = $"{queueName}.{this.ServiceId}.{i}";
                await this.CreateConsumer(index, myQueue, queueName, consumerId, nodeIds, localConsumerIds);
                index++;
            }
        }

        //创建有状态队列SAC等待消费者
        foreach (var myQueue in statefullQueues)
        {
            for (int i = 0; i < myQueue.WorkloadTotal; i++)
            {
                for (int workloadIndex = 1; workloadIndex < this.sacCount; workloadIndex++)
                {
                    var queueName = $"{myQueue.QueueId}.{i}";
                    var consumerId = $"{queueName}.{this.ServiceId}.{workloadIndex}";
                    await this.CreateConsumer(index, myQueue, queueName, consumerId, nodeIds, localConsumerIds);
                    index++;
                }
            }
        }

        //创建转发队列
        if (this.queues.Exists(f => f.IsStateful && f.AppId == this.AppId))
        {
            for (int workloadIndex = 0; workloadIndex < this.sacCount; workloadIndex++)
            {
                await this.CreateTransferConsumer(workloadIndex, nodeIds, localConsumerIds);
                index++;
            }
        }
        this.lastNodeIds = currentNodeIds;
        this.queues = dbQueues;
        this.bindings = dbBindings;

        //最后处理多余的消费者
        var queueNames = this.consumers.Keys.ToList();
        foreach (var myQueueName in queueNames)
        {
            if (!this.consumers.TryGetValue(myQueueName, out rabbitConsumers))
                continue;
            if (rabbitConsumers == null || rabbitConsumers.Count == 0)
            {
                this.consumers.TryRemove(myQueueName, out _);
                this.shutdownQueues.TryRemove(myQueueName, out _);
                continue;
            }
            if (localConsumerIds.TryGetValue(myQueueName, out var myConsumerIds))
            {
                this.shutdownQueues.TryRemove(myQueueName, out _);
                //队列存在，删除不是本节点的消费者
                var removedConsumers = rabbitConsumers.FindAll(f => !myConsumerIds.Contains(f.ConsumerId));
                foreach (var removedConsumer in removedConsumers)
                {
                    rabbitConsumers.Remove(removedConsumer);
                    await removedConsumer.ShutdownAsync();
                    Console.WriteLine($"多余消费者{removedConsumer.ConsumerId}已关闭");
                }
            }
            else if (allQueueNames.Contains(myQueueName))
            {
                //队列仍然有效，只是已经迁移到其他节点。立即取消本节点订阅，让SAC切换到新节点。
                foreach (var myConsumer in rabbitConsumers)
                    await myConsumer.ShutdownAsync();
                rabbitConsumers.Clear();
                this.shutdownQueues.TryRemove(myQueueName, out _);
                this.consumers.TryRemove(myQueueName, out _);
                Console.WriteLine($"队列{myQueueName}已迁移到其他节点，本节点消费者已关闭");
            }
            else
            {
                //缩容有状态队列，不应该存在的队列，等待队列没有消息后，再过2个心跳周期，删除所有消费者，此过程中，0
                var hasMessage = false;
                foreach (var myConsumer in rabbitConsumers)
                {
                    if (myConsumer.IsBusying)
                    {
                        hasMessage = true;
                        break;
                    }
                    if (await myConsumer.MessageCount() > 0)
                    {
                        hasMessage = true;
                        break;
                    }
                }
                if (hasMessage)
                {
                    this.shutdownQueues.TryRemove(myQueueName, out _);
                    continue;
                }

                //过2个心跳周期，再删除消费者
                if (!this.shutdownQueues.TryGetValue(myQueueName, out var lastUpdateTime))
                    this.shutdownQueues.TryAdd(myQueueName, lastUpdateTime = DateTime.UtcNow);
                if (DateTime.UtcNow.Subtract(lastUpdateTime) < this.heartbeatCycle * 2)
                    continue;
                foreach (var myConsumer in rabbitConsumers)
                    await myConsumer.ShutdownAsync();
                rabbitConsumers.Clear();
                this.shutdownQueues.TryRemove(myQueueName, out _);
                this.consumers.TryRemove(myQueueName, out _);
                Console.WriteLine($"队列{myQueueName}所有消费者已关闭");
            }
        }
    }
    private async Task ShutdownConsumersAsync()
    {
        var myConsumers = this.consumers.Values
            .SelectMany(f => f).Distinct().ToList();
        if (this.heartbeatConsumer != null)
            myConsumers.Add(this.heartbeatConsumer);
        if (this.rpcConsumer != null)
            myConsumers.Add(this.rpcConsumer);
        await Task.WhenAll(myConsumers.Distinct()
            .Select(this.ShutdownConsumerAsync));
    }
    private async Task ShutdownConsumerAsync(RabbitConsumer consumer)
    {
        try
        {
            await consumer.ShutdownAsync();
        }
        catch (Exception ex)
        {
            this.logger.LogTagError("MessageDriven", ex, $"关闭消费者失败, ConsumerId: {consumer.ConsumerId}");
        }
    }
    private async Task CleanupAsync()
    {
        this.consumers.Clear();
        this.heartbeats.Clear();
        this.shutdownQueues.Clear();
        foreach (var rpcWaiter in this.rpcWaiters.Values)
            rpcWaiter.Waiter.TrySetException(new Exception("MessageDrivenService已经关闭"));
        this.rpcWaiters.Clear();
        if (this.rabbitProducer != null)
            await this.rabbitProducer.ShutdownAsync();
        this.stopTokenSource?.Dispose();
        this.stopTokenSource = null;
    }
    private async Task CreateConsumer(int index, Queue myQueue, string queueName,
        string consumerId, List<string> nodeIds, Dictionary<string, List<string>> localConsumerIds)
    {
        var nodeId = nodeIds.Count > 1 ? nodeIds[index % nodeIds.Count] : this.ServiceId;
        if (nodeId != this.ServiceId) return;

        var rabbitConsumers = this.consumers.GetOrAdd(queueName, f => new List<RabbitConsumer>());
        if (!localConsumerIds.TryGetValue(queueName, out var myConsumerIds))
            localConsumerIds.TryAdd(queueName, myConsumerIds = new());
        myConsumerIds.Add(consumerId);
        var myRabbitConsumer = rabbitConsumers.Find(f => f.ConsumerId == consumerId);
        if (myRabbitConsumer != null)
        {
            myRabbitConsumer.IsLogEnabled = myQueue.IsLogEnabled;
            if (!myRabbitConsumer.IsActivated || myRabbitConsumer.PrefetchCount != myQueue.PrefetchCount)
            {
                myRabbitConsumer.PrefetchCount = myQueue.PrefetchCount;
                await myRabbitConsumer.RestartAsync();
            }
        }
        else
        {
            //新增消费者，直接启动
            var exchangeHandlers = this.consumerHandlers[queueName];
            myRabbitConsumer = new RabbitConsumer(queueName, consumerId, this, this.serviceProvider, QueueType.Message, myQueue.PrefetchCount, exchangeHandlers) { IsLogEnabled = myQueue.IsLogEnabled };
            rabbitConsumers.Add(myRabbitConsumer);
            await myRabbitConsumer.StartAsync();
            var queueType = myQueue.IsStateful ? "有" : "无";
            Console.WriteLine($"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}, {queueType}状态队列{queueName} 消费者{consumerId}已启动，当前消费者数量：{rabbitConsumers.Count}");
        }
    }
    private async Task CreateTransferConsumer(int index, List<string> nodeIds, Dictionary<string, List<string>> consumerIds)
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
                await myRabbitConsumer.RestartAsync();
        }
        else
        {
            //新增消费者，直接启动
            myRabbitConsumer = new RabbitConsumer(queueName, $"{queueName}.{this.ServiceId}.{index}", this, this.serviceProvider, QueueType.Transfer);
            rabbitConsumers.Add(myRabbitConsumer);
            await myRabbitConsumer.StartAsync();
            Console.WriteLine($"转发队列消费者{queueName}已启动，当前消费者数量：{rabbitConsumers.Count}");
        }
    }
    private void EnsureAvailable()
    {
        if (!this.IsEnabled)
            throw new InvalidOperationException("MessageDriven is disabled.");
        if (Volatile.Read(ref this.appState) == 2)
            throw new ObjectDisposedException(nameof(MessageDrivenService), "MessageDriven is stopping.");
    }
    private async Task SendHeartbeat()
    {
        //正在关闭中的pod不再发送心跳消息，只处理发送业务消息
        if (Volatile.Read(ref this.appState) == 2) return;
        if (DateTime.UtcNow - this.lastHeartbeatTime >= this.heartbeatCycle)
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
            this.lastHeartbeatTime = DateTime.UtcNow;
        }
    }
}