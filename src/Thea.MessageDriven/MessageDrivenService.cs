using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Thea.Logging;

namespace Thea.MessageDriven;

class MessageDrivenService : IMessageDriven, IHostedService
{
    private Task mainTask;
    private Task heartbeatTask = Task.CompletedTask;
    private Task producerTask = Task.CompletedTask;
    private CancellationTokenSource stopTokenSource;
    private readonly SemaphoreSlim lifecycleLocker = new(1, 1);
    private Task startingTask = Task.CompletedTask;

    private readonly TaskCompletionSource<bool> readyToStart = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly ConcurrentDictionary<string, List<RabbitConsumer>> consumers = new();
    private readonly ConcurrentDictionary<string, TimedConsumers> shutdownConsumers = new();
    private readonly ConcurrentQueue<Message> failedMessages = new();
    private readonly ConcurrentDictionary<string, DateTime> heartbeats = new();
    private readonly ConcurrentDictionary<string, RpcWaiter> rpcWaiters = new();
    private Channel<Message> messageChannel = null;
    private Channel<ExecLog> logsChannel = null;
    private bool isProducer = false;
    private bool hasConsumer = false;
    private bool isRpcConsumer = false;
    private readonly bool isAllowCreateQueue = false;
    private readonly bool isAllowCreateExchange = false;
    private readonly bool isAllowCreateBinding = false;

    private List<Binding> bindings = new();
    private List<Queue> queues = new();
    private List<string> localQueueIds;
    private List<string> localExchangeIds;
    private List<ExchangeTransfer> exchangeTransfers = new();

    private RabbitConsumer heartbeatConsumer;
    private RabbitConsumer rpcConsumer;
    private string lastNodeIds = null;
    private readonly int sacCount = 2;
    private readonly int messagePoolSize = 50000;

    private readonly Dictionary<string, Dictionary<string, MethodInfo>> consumerHandlers = new();
    private readonly IServiceProvider serviceProvider;
    private readonly ILogger<MessageDrivenService> logger;
    private IMessageDrivenRepository repository;
    private DateTime lastHeartbeatTime = DateTime.MinValue;
    private DateTime lastInitedTime = DateTime.MinValue;
    private DateTime lastClearRpcTime = DateTime.MinValue;
    private DateTime lastLoggedTime = DateTime.MinValue;
    // 0: none, 1: starting, 2: running, 3: stopping, 4: stopped
    private int appState = 0;
    private int isChangeConsumers = 0;

    internal readonly TimeSpan heartbeatCycle;
    internal RabbitProducer rabbitProducer;
    internal List<AmqpTcpEndpoint> tcpEndPoints;
    public string AppId { get; private set; }
    public string ServiceId { get; private set; }
    public string RpcQueue { get; private set; }
    internal bool IsEnabled { get; private set; }

    public MessageDrivenService(IServiceProvider serviceProvider)
    {
        this.logger = serviceProvider.GetService<ILogger<MessageDrivenService>>();
        var configuration = serviceProvider.GetService<IConfiguration>();
        this.IsEnabled = configuration.GetValue("MessageDriven:IsEnabled", true);
        if (!this.IsEnabled) return;

        this.serviceProvider = serviceProvider;
        this.ServiceId = ObjectId.NewId();
        this.AppId = configuration.GetValue<string>("AppId");
        this.RpcQueue = $"{Consts.RpcExchange}.{this.AppId}.{this.ServiceId}";
        var endPoints = configuration.GetSection("MessageDriven:EndPoints").Get<string[]>();
        if (endPoints == null || endPoints.Length == 0)
            throw new Exception("未设置MessageDriven:EndPoints，无法初始化MessageDrivenService对象");
        this.tcpEndPoints = endPoints.Select(f => AmqpTcpEndpoint.Parse(f)).ToList();
        this.isAllowCreateQueue = configuration.GetValue("MessageDriven:IsAllowCreateQueue", true);
        this.isAllowCreateExchange = configuration.GetValue("MessageDriven:IsAllowCreateExchange", true);
        this.isAllowCreateBinding = configuration.GetValue("MessageDriven:IsAllowCreateBinding", true);
        this.heartbeatCycle = TimeSpan.FromSeconds(configuration.GetValue("MessageDriven:Heartbeat", 10));
        this.messagePoolSize = configuration.GetValue("MessageDriven:MessagePoolSize", 50000);

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
            this.logger?.LogWarning("MessageDriven is disabled.");
            return;
        }
        if (!this.isProducer && !this.hasConsumer)
            throw new Exception("消息驱动，至少使用生产者或是消费者，至少调用一次UseProducer/UseSubscriber/UseStatefulConsumer方法");
        //等待start
        await this.readyToStart.Task.WaitAsync(cancellationToken);

        Task startingTask;
        await this.lifecycleLocker.WaitAsync(cancellationToken);
        try
        {
            var currentState = Volatile.Read(ref this.appState);
            if (currentState == 3) throw new InvalidOperationException("正在关闭中，请稍后再试");
            if (currentState is 1 or 2) return;
            Interlocked.Exchange(ref this.appState, 1);
            startingTask = this.startingTask = this.CompleteStartAsync();
            ObserveFailure(startingTask);
        }
        finally { this.lifecycleLocker.Release(); }
        await startingTask.WaitAsync(cancellationToken);
    }
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (!this.IsEnabled) return;
        Task stopingTask;
        await this.lifecycleLocker.WaitAsync(cancellationToken);
        try
        {
            var currentState = Volatile.Read(ref this.appState);
            if (currentState is 3 or 4) return;

            Interlocked.Exchange(ref this.appState, 3);
            stopingTask = this.CompleteStopAsync();
            ObserveFailure(stopingTask);
        }
        finally { this.lifecycleLocker.Release(); }
        await stopingTask.WaitAsync(cancellationToken);
    }
    private static void ObserveFailure(Task task) => task.ContinueWith(completed =>
    {
        _ = completed.Exception;
    }, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    private async Task HeartbeatLoopAsync()
    {
        //使用独立的StopToken，关闭中还会有消费者消费，可能需要
        while (!this.stopTokenSource.IsCancellationRequested)
        {
            if (DateTime.UtcNow.Subtract(this.lastHeartbeatTime) >= this.heartbeatCycle)
            {
                try
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
                catch (Exception ex)
                {
                    this.logger.LogTagError("MessageDriven", ex, "发送心跳失败");
                }
            }
            await Task.Delay(100);
        }
    }
    private async Task ExecuteAsync()
    {
        while (!this.stopTokenSource.IsCancellationRequested)
        {
            try
            {
                if (DateTime.UtcNow.Subtract(this.lastInitedTime) > TimeSpan.FromSeconds(30))
                {
                    //有消费者，按照消费者创建的生命周期初始化，否则就按照心跳时间周期初始化
                    await this.Initialize(false);
                    this.lastInitedTime = DateTime.UtcNow;
                }
                if (!this.rpcWaiters.IsEmpty && DateTime.UtcNow.Subtract(this.lastClearRpcTime).TotalSeconds > 5)
                {
                    var messageIds = this.rpcWaiters.Keys.ToList();
                    foreach (var messageId in messageIds)
                    {
                        if (!this.rpcWaiters.TryGetValue(messageId, out var rpcWaiter))
                            continue;
                        var timeElapsed = DateTime.UtcNow.Subtract(rpcWaiter.CreatedAt).TotalSeconds;
                        if (timeElapsed < rpcWaiter.TimeoutSeconds)
                            continue;
                        this.rpcWaiters.TryRemove(messageId, out _);
                        //只结束超时的RPC请求，不做清理工作，清理工作在Request方法中处理
                        rpcWaiter.Waiter.TrySetException(new TimeoutException(rpcWaiter.TimeoutMessage));
                    }
                    this.lastClearRpcTime = DateTime.UtcNow;
                }
                if (this.hasConsumer)
                {
                    var logs = new List<ExecLog>();
                    while (logs.Count < 100)
                    {
                        if (DateTime.UtcNow.Subtract(this.lastLoggedTime) >= TimeSpan.FromSeconds(10)
                            || DateTime.UtcNow.Subtract(this.lastInitedTime) > TimeSpan.FromSeconds(30)
                            || !this.rpcWaiters.IsEmpty && DateTime.UtcNow.Subtract(this.lastClearRpcTime) > TimeSpan.FromSeconds(5))
                            break;
                        if (this.logsChannel.Reader.TryRead(out var log))
                            logs.Add(log);
                        else
                        {
                            var remaining = TimeSpan.FromSeconds(10).Subtract(DateTime.UtcNow.Subtract(this.lastLoggedTime))
                                .Min(TimeSpan.FromSeconds(30).Subtract(DateTime.UtcNow.Subtract(this.lastInitedTime)))
                                .Min(!this.rpcWaiters.IsEmpty, TimeSpan.FromSeconds(5) - DateTime.UtcNow.Subtract(this.lastClearRpcTime));
                            if (remaining <= TimeSpan.Zero) break;
                            if (!await this.ReadLog(logs, remaining))
                                break;
                        }
                    }
                    try { if (logs.Count > 0) await this.repository.WriteLogs(logs); }
                    catch (Exception ex) { Console.WriteLine($"MessageDriven写日志失败：{ex.Message}"); }
                    this.lastLoggedTime = DateTime.UtcNow;
                }
                else
                {
                    var remaining = TimeSpan.FromSeconds(30).Subtract(DateTime.UtcNow.Subtract(this.lastInitedTime))
                        .Min(!this.rpcWaiters.IsEmpty, TimeSpan.FromSeconds(5) - DateTime.UtcNow.Subtract(this.lastClearRpcTime));
                    await this.Delay(remaining);
                }
            }
            catch (Exception ex)
            {
                var exception = ex.InnerException ?? ex;
                this.logger.LogTagError("MessageDriven", exception, $"ExecuteAsync is failled");
            }
        }
    }
    private async Task<bool> ReadLog(List<ExecLog> logs, TimeSpan timeout)
    {
        try
        {
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(this.stopTokenSource.Token);
            timeoutSource.CancelAfter(timeout);
            if (await this.logsChannel.Reader.WaitToReadAsync(timeoutSource.Token)
                && this.logsChannel.Reader.TryRead(out var log))
            {
                logs.Add(log);
                return true;
            }
        }
        catch (OperationCanceledException)
            when (!this.stopTokenSource.IsCancellationRequested)
        {
            // flush timeout
        }
        return false;
    }
    public async Task ProducerLoopAsync()
    {
        var statefulBindings = this.bindings.FindAll(f =>
            f.IsStateful).ToDictionary(f => f.ExchangeId, f => f);
        var transferQueues = this.queues.FindAll(f =>
            f.IsTransfer).ToDictionary(f => f.AppId, f => f);
        while (await this.messageChannel.Reader.WaitToReadAsync())
        {
            try
            {
                while (this.messageChannel.Reader.TryRead(out var message))
                {
                    try
                    {
                        BasicProperties properties = null;
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
                                var jsonMessage = message.Body.ToString();
                                if (statefulBindings.TryGetValue(message.Exchange, out var myBinding))
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
                                        var queueName = $"{Consts.TransferExchange}.{myBinding.AppId}.0";
                                        if (transferQueues.TryGetValue(myBinding.AppId, out var queueInfo)
                                            && queueInfo.WorkloadTotal > 1 && queueInfo.IsConfirmed)
                                        {
                                            var routingKey = JumpConsistentHash.GetBucket(message.RoutingKey, queueInfo.WorkloadTotal);
                                            queueName = $"{Consts.TransferExchange}.{myBinding.AppId}.{routingKey}";
                                        }
                                        await this.rabbitProducer.PublishAsync(Consts.DefaultExchange, queueName, properties, jsonMessage);
                                    }
                                }
                                //如果是无状态队列的消息，直接发送交换机
                                else await this.rabbitProducer.PublishAsync(message.Exchange, message.RoutingKey, properties, jsonMessage);
                                message.Waiter?.TrySetResult(true);
                                break;
                            case Consts.ChangeConsumers:
                                //本地pod消费者变更通知已更新，相应的队列和绑定都已创建，其他的pod不同步也没有关系
                                (statefulBindings, transferQueues) = ((Dictionary<string, Binding>, Dictionary<string, Queue>))message.Body;
                                break;
                            case Consts.RpcResponse:
                            case Consts.RpcFailure:
                                properties = new BasicProperties
                                {
                                    Persistent = true,
                                    Type = message.Type,
                                    DeliveryMode = DeliveryModes.Persistent,
                                    AppId = this.AppId,
                                    MessageId = message.MessageId,
                                    ReplyTo = message.ReplyTo,
                                    CorrelationId = message.MessageId,
                                    Headers = new Dictionary<string, object> { { "TraceId", message.TraceId } }
                                };
                                await this.rabbitProducer.PublishAsync(Consts.DefaultExchange, message.ReplyTo, properties, message.Body as string);
                                message.Waiter?.TrySetResult(true);
                                break;
                        }
                    }
                    catch (Exception ex)
                    {
                        message.RetryTimes++;
                        var exception = ex.InnerException ?? ex;
                        if (message.RetryTimes > 3)
                        {
                            var errMessage = $"发送消息失败，RetryTimes: {message.RetryTimes - 1}，Message: {message.ToJson()}";
                            this.logger.LogTagError("MessageDriven", exception, errMessage);
                            message.Waiter?.TrySetException(new Exception(errMessage, exception));
                            continue;
                        }
                        if (Volatile.Read(ref this.appState) == 3)
                        {
                            //消费者发过来的消息，直接抛异常nack回队列，应用端发过来的消息，直接上报应用端
                            var errMessage = $"发送消息失败，RetryTimes: {message.RetryTimes - 1}，Message: {message.ToJson()}";
                            this.logger.LogTagError("MessageDriven", exception, errMessage);
                            message.Waiter?.TrySetException(new Exception(errMessage, exception));
                        }
                        else
                        {
                            if (this.messageChannel.Writer.TryWrite(message))
                                continue;
                            this.failedMessages.Enqueue(message);
                        }
                    }
                }
                while (this.failedMessages.TryPeek(out var failedMessage))
                {
                    if (this.messageChannel.Writer.TryWrite(failedMessage))
                    {
                        this.failedMessages.TryDequeue(out _);
                        continue;
                    }
                    //阻塞了就切换到发送消息
                    break;
                }
            }
            catch (Exception ex)
            {
                var exception = ex.InnerException ?? ex;
                this.logger.LogTagError("MessageDriven", exception, $"ProducerLoopAsync is failled");
            }
        }
    }
    private async Task CompleteStartAsync()
    {
        //异常不再启动
        int targetSate = 1;
        try
        {
            await this.Register();

            this.stopTokenSource = new();
            if (this.hasConsumer)
            {
                this.heartbeatTask = this.HeartbeatLoopAsync();
                this.heartbeats[this.ServiceId] = DateTime.UtcNow;
            }
            await this.Initialize(true);
            this.lastInitedTime = DateTime.UtcNow;
            if (this.isProducer) this.producerTask = this.ProducerLoopAsync();
            this.mainTask = this.ExecuteAsync();

            //启动成功，从0/4 -> 1 -> 2
            targetSate = 2;
            Console.WriteLine($"ServiceId：{this.ServiceId}，MessageDriven is started！");
        }
        catch (Exception ex)
        {
            targetSate = 0;
            Console.WriteLine($"ServiceId：{this.ServiceId}，MessageDriven startup failed，{ex}");
            throw;
        }
        finally
        {
            await this.lifecycleLocker.WaitAsync();
            try { Interlocked.Exchange(ref this.appState, targetSate); }
            finally { this.lifecycleLocker.Release(); }
        }
    }
    private async Task CompleteStopAsync()
    {
        await this.startingTask;

        var errors = new List<Exception>();
        try { await this.ShutdownAsync(); }
        catch (Exception ex) { errors.Add(ex); }
        try { await this.CleanupAsync(); }
        catch (Exception ex) { errors.Add(ex); }
        if (errors.Count > 0)
        {
            var exception = errors.Count > 1 ? new AggregateException(errors) : errors[0];
            Console.WriteLine($"stopping is failed，{exception}");
            throw exception;
        }

        await this.lifecycleLocker.WaitAsync();
        Interlocked.Exchange(ref this.appState, 4);
        this.lifecycleLocker.Release();
        Console.WriteLine($"ServiceId：{this.ServiceId}，MessageDriven is stopped！");
    }
    private async Task ShutdownAsync()
    {
        var errors = new List<Exception>();
        async Task Attempt(Func<Task> action)
        {
            try { await action(); }
            catch (Exception ex) { errors.Add(ex); }
        }
        // 先停止控制循环，防止关闭快照之后又创建新的消费者。
        await Attempt(() => { this.stopTokenSource?.Cancel(); return Task.CompletedTask; });
        await Attempt(() => Task.WhenAll([this.mainTask, this.heartbeatTask]));

        var consumers = this.consumers.Values.SelectMany(f => f)
            .Concat(this.shutdownConsumers.Values.SelectMany(x => x.Consumers))
            .Concat([this.heartbeatConsumer]).Where(f => f != null).Distinct().ToList();
        await Attempt(() => Task.WhenAll(consumers.Select(f => f.ShutdownAsync())));
        await Attempt(() => this.rpcConsumer?.ShutdownAsync() ?? Task.CompletedTask);

        this.messageChannel?.Writer.TryComplete();
        await Attempt(() => this.producerTask);
        if (errors.Count > 0) throw new AggregateException(errors);
    }
    private async Task CleanupAsync()
    {
        this.consumers.Clear();
        this.shutdownConsumers.Clear();
        this.heartbeats.Clear();
        foreach (var rpcWaiter in this.rpcWaiters.Values)
            rpcWaiter.Waiter.TrySetException(new Exception("MessageDrivenService已经关闭"));
        this.rpcWaiters.Clear();
        try
        {
            if (this.rabbitProducer != null)
                await this.rabbitProducer.ShutdownAsync();
        }
        finally
        {
            this.rabbitProducer = null;
            this.stopTokenSource?.Dispose();
            this.stopTokenSource = null;
            this.heartbeatConsumer = null;
            this.rpcConsumer = null;
            this.mainTask = this.heartbeatTask = this.producerTask = Task.CompletedTask;
            this.lastNodeIds = null;
            this.lastHeartbeatTime = DateTime.MinValue;
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
            Body = message.ToJson(),
            Waiter = new(TaskCreationOptions.RunContinuationsAsynchronously)
        };
        await this.messageChannel.Writer.WriteAsync(theaMessage, cancellationToken);
        await theaMessage.Waiter.Task.WaitAsync(TimeSpan.FromSeconds(30));
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
            Body = request.ToJson(),
            Waiter = new(TaskCreationOptions.RunContinuationsAsynchronously)
        };
        await this.messageChannel.Writer.WriteAsync(theaMessage, cancellationToken);
        await theaMessage.Waiter.Task.WaitAsync(TimeSpan.FromSeconds(30));
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
            ReplyTo = $"{Consts.RpcExchange}.{this.AppId}.{this.ServiceId}",
            Type = Consts.RpcMessage,
            TraceId = traceId,
            Exchange = exchange,
            RoutingKey = routingKey,
            Body = request.ToJson(),
            Waiter = new(TaskCreationOptions.RunContinuationsAsynchronously)
        };
        var exMessage = $"RPC请求超时, 耗时{timeoutSeconds}s, message: {request.ToJson()}, exchange: {exchange}, routingKey: {routingKey}";
        var rpcWaiter = new RpcWaiter { MessageId = theaMessage.MessageId, TimeoutSeconds = timeoutSeconds, TimeoutMessage = exMessage };
        this.rpcWaiters.TryAdd(theaMessage.MessageId, rpcWaiter);
        var timeout = TimeSpan.FromSeconds(timeoutSeconds);
        await this.messageChannel.Writer.WriteAsync(theaMessage, cancellationToken);
        await theaMessage.Waiter.Task.WaitAsync(timeout);
        //Console.WriteLine($"RpcMessage,Request Message, MessageId: {theaMessage.MessageId}, From:{this.ServiceId}, DateTime: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}");
        var rpcMessage = await rpcWaiter.Waiter.WaitAsync(timeout, exMessage, cancellationToken);
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
            Body = message.ToJson(),
            Waiter = new(TaskCreationOptions.RunContinuationsAsynchronously)
        };
        await this.messageChannel.Writer.WriteAsync(theaMessage, cancellationToken);
        await theaMessage.Waiter.Task.WaitAsync(TimeSpan.FromSeconds(30));
    }
    public void UseProducer() => this.isProducer = true;
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
                IsDelay = false,

                AppId = this.AppId,
                IsStateful = true
            });
        }
    }
    public void UseTransferConsumer(int workloadTotal = 1, bool isSingleActiveConsumer = true, bool isQuorumQueue = true)
    {
        var queueId = $"{Consts.TransferExchange}.{this.AppId}";
        var myQueue = this.queues.Find(f => f.QueueId == queueId);
        if (myQueue == null)
        {
            this.queues.Add(new Queue
            {
                QueueId = queueId,
                AppId = this.AppId,
                IsQuorumQueue = isQuorumQueue,
                IsStateful = true,
                IsTransfer = true,
                IsSingleActiveConsumer = isSingleActiveConsumer,
                PrefetchCount = 250,
                WorkloadTotal = workloadTotal,
                IsLogEnabled = false
            });
        }
        else
        {
            myQueue.IsStateful = true;
            myQueue.IsTransfer = true;
            myQueue.IsQuorumQueue = isQuorumQueue;
            myQueue.IsSingleActiveConsumer = isSingleActiveConsumer;
            myQueue.PrefetchCount = 250;
            myQueue.WorkloadTotal = workloadTotal;
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
        var bindingType = isDelay ? Consts.DelayBindingType : Consts.TopicBindingType;
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
                IsDelay = isDelay,

                AppId = this.AppId,
                IsStateful = false
            });
        }
    }
    public void UseRpcConsumer(MethodInfo methodInfo = null)
    {
        this.isProducer = true;
        this.isRpcConsumer = true;
        if (methodInfo != null)
        {
            var myConsumerHandler = this.consumerHandlers[this.RpcQueue] = new();
            myConsumerHandler[Consts.RpcExchange] = methodInfo;
        }
    }
    public async Task Change(string queueId, int workloadTotal, int? prefetchCount = null, bool? isLogEnabled = null)
    {
        this.EnsureAvailable();
        if (workloadTotal <= 0) throw new Exception("workloadTotal必须大于0");

        (var dbQueues, _) = await this.repository.GetSettings(false);
        var myQueue = dbQueues.Find(f => f.QueueId == queueId);
        if (myQueue == null) throw new Exception($"队列{queueId}不存在");
        myQueue.WorkloadTotal = workloadTotal;
        if (prefetchCount.HasValue)
            myQueue.PrefetchCount = prefetchCount.Value;
        if (isLogEnabled.HasValue)
            myQueue.IsLogEnabled = isLogEnabled.Value;
        myQueue.IsConfirmed = !myQueue.IsTransfer;
        await this.repository.Change(myQueue);
    }
    public async Task RemoveQueue(string queueId, int minSuffixIndex, int maxSuffixIndex)
    {
        this.EnsureAvailable();
        for (int i = minSuffixIndex; i <= maxSuffixIndex; i++)
        {
            var queueName = $"{queueId}.{i}";
            //var messageCount = await this.rabbitProducer.MessageCount(queueName);
            //if (messageCount > 0) continue;
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
            case Consts.UserMessage:
            case Consts.RpcMessage:
                //转发消费者返回的消息
                await this.messageChannel.Writer.WriteAsync(message);
                await message.Waiter.Task;
                break;
            case Consts.Logs:
                await this.logsChannel.Writer.WriteAsync(message.Body as ExecLog);
                break;
            case Consts.RpcResponse:
            case Consts.RpcFailure:
                //用户业务消费者返回的消息
                if (!string.IsNullOrEmpty(message.ReplyTo))
                {
                    await this.messageChannel.Writer.WriteAsync(message);
                    await message.Waiter.Task;
                }
                //rpc结果消费者返回的消息
                if (this.rpcWaiters.TryRemove(message.MessageId, out var rpcWaiter))
                    rpcWaiter.Waiter.TrySetResult(message);
                break;
            case Consts.ChangeConsumers:
                //心跳队列发来的消息
                await this.messageChannel.Writer.WriteAsync(message);
                break;
        }
    }
    private async Task Register()
    {
        if (this.queues.Count > 0 && this.bindings.Count > 0)
        {
            this.localQueueIds = this.queues.Select(f => f.QueueId).ToList();
            this.localExchangeIds = this.bindings.Select(f => f.ExchangeId).ToList();
            await this.repository.Register(this.queues, this.bindings);
        }
        if (this.isProducer)
        {
            this.messageChannel = Channel.CreateBounded<Message>(new BoundedChannelOptions(this.messagePoolSize)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleWriter = false,
                SingleReader = true
            });
        }
        if (this.hasConsumer)
        {
            this.logsChannel = Channel.CreateBounded<ExecLog>(new BoundedChannelOptions(300)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleWriter = false,
                SingleReader = true
            });
        }
        this.rabbitProducer = new RabbitProducer(this, this.serviceProvider);
        await this.rabbitProducer.CreateProducer();

        //创建RPC消费者
        string queueId = null;
        if (this.isRpcConsumer)
        {
            if (!this.isAllowCreateQueue)
                throw new Exception("未配置允许创建队列，无法创建RPC结果队列，请检查配置项：IsAllowCreateQueue");

            queueId = $"{Consts.RpcExchange}.{this.AppId}";
            this.rpcConsumer = new RabbitConsumer(new DispatchInfo
            {
                QueueType = QueueType.RpcResult,
                Queue = new Queue { QueueId = queueId },
                QueueName = $"{queueId}.{this.ServiceId}"
            }, this, this.serviceProvider);
            await this.rpcConsumer.StartAsync();
        }
        //创建队列和绑定，需要捞取数据库或是配置中心的最新信息
        //包含所有应用的队列和绑定信息，交换机可以多个应用公用，每个应用有自己的队列及处理程序
        if (!this.hasConsumer) return;

        (var dbQueues, var dbBindings) = await this.repository.GetSettings(false);
        //根据本地配置，更新队列状态
        if (this.hasConsumer)
        {
            foreach (var myQueue in this.queues)
            {
                var dbQueue = dbQueues.Find(f => f.QueueId == myQueue.QueueId);
                dbQueue.IsStateful = myQueue.IsStateful;
                dbQueue.IsTransfer = myQueue.IsTransfer;
                dbQueue.IsQuorumQueue = myQueue.IsQuorumQueue;
                dbQueue.IsSingleActiveConsumer = myQueue.IsSingleActiveConsumer;
                //以数据库配置为主
                dbQueue.LastWorkloadTotal = dbQueue.WorkloadTotal;
                dbQueue.ChangeType = ChangeType.None;
            }
        }

        //数据库中本应用配置可能会多于本地配置，本应用的配置以本地为主，其他应用以数据库为主
        var myQueues = dbQueues.FindAll(f => this.localQueueIds.Contains(f.QueueId));
        var myBindings = dbBindings.FindAll(f => this.localExchangeIds.Contains(f.ExchangeId));

        string queueName = null;
        if (this.hasConsumer)
        {
            //一个交换机对应一块业务
            //一个交换机顶多绑定一个有状态队列，但可以同时绑定多个无状态队列
            //允许多个交换机绑定到同一个有状态队列
            var exchangeQueues = myBindings.Where(f => f.IsStateful)
                .GroupBy(f => f.ExchangeId).ToDictionary(f => f.Key, f => f.Select(t => t.QueueId).ToList())
                .Where(f => f.Value.Count > 1).ToList();
            if (exchangeQueues.Count > 0)
            {
                foreach (var exchangeQueue in exchangeQueues)
                    throw new Exception($"与交换机{exchangeQueue.Key}绑定的有状态队列只能一个，目前绑定的有状态队列[{string.Join(',', exchangeQueue.Value)}]");
            }
        }

        //创建交换机
        if (this.isAllowCreateExchange)
        {
            //工作交换机
            var distBindings = myBindings.Select(f => new { f.ExchangeId, f.BindType, f.IsDelay }).Distinct().ToList();
            foreach (var myBinding in distBindings)
                await this.rabbitProducer.CreateExchange(myBinding.ExchangeId, myBinding.BindType, myBinding.IsDelay);
            //心跳交换机
            await this.rabbitProducer.CreateExchange(Consts.HeartbeatExchange, Consts.TopicBindingType);
        }

        //创建队列      
        if (this.isAllowCreateQueue)
        {
            //创建工作负载队列和转发队列
            foreach (var myQueue in myQueues)
            {
                if (myQueue.IsStateful)
                {
                    var queueType = myQueue.IsTransfer ? QueueType.Transfer : QueueType.Message;
                    for (int i = 0; i < myQueue.WorkloadTotal; i++)
                    {
                        queueName = $"{myQueue.QueueId}.{i}";
                        await this.rabbitProducer.CreateQueue(queueName, myQueue.IsQuorumQueue, myQueue.IsSingleActiveConsumer, false);
                    }
                }
                else await this.rabbitProducer.CreateQueue(myQueue.QueueId, myQueue.IsQuorumQueue, false, false);
            }
            //创建独占心跳队列和消费者
            queueId = $"{Consts.HeartbeatExchange}.{this.AppId}";
            this.heartbeatConsumer = new RabbitConsumer(new DispatchInfo
            {
                QueueType = QueueType.Heartbeat,
                Queue = new Queue { QueueId = queueId },
                QueueName = $"{queueId}.{this.ServiceId}"
            }, this, this.serviceProvider);
            await this.heartbeatConsumer.StartAsync();
        }

        //创建队列绑定
        if (this.isAllowCreateBinding)
        {
            //创建工作负载队列绑定
            foreach (var myBinding in myBindings)
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
    private async Task Initialize(bool isFirst)
    {
        if (!isFirst && Volatile.Read(ref this.appState) >= 3)
            return;
        if (Interlocked.CompareExchange(ref this.isChangeConsumers, 1, 0) == 1)
            return;

        try
        {
            (var dbQueues, var dbBindings) = await this.repository.GetSettings(!isFirst);
            //根据本地配置，更新队列状态
            foreach (var myQueue in this.queues)
            {
                var dbQueue = dbQueues.Find(f => f.QueueId == myQueue.QueueId);
                dbQueue.IsStateful = myQueue.IsStateful;
                dbQueue.IsTransfer = myQueue.IsTransfer;
                dbQueue.IsQuorumQueue = myQueue.IsQuorumQueue;
                dbQueue.IsSingleActiveConsumer = myQueue.IsSingleActiveConsumer;

                //确定变更类型，扩容/缩容
                dbQueue.LastWorkloadTotal = myQueue.WorkloadTotal;
                if (dbQueue.WorkloadTotal == myQueue.WorkloadTotal)
                    dbQueue.ChangeType = ChangeType.None;
                else if (dbQueue.WorkloadTotal > myQueue.WorkloadTotal)
                    dbQueue.ChangeType = ChangeType.ScalingUp;
                else dbQueue.ChangeType = ChangeType.ScalingDown;
            }
            var changedQueues = dbQueues;
            var changedBindings = dbBindings;
            var removeKeys = new List<string>();
            if (this.hasConsumer)
            {
                changedQueues = dbQueues.FindAll(f => this.localQueueIds.Contains(f.QueueId) || f.AppId != this.AppId);
                changedBindings = dbBindings.FindAll(f => this.localExchangeIds.Contains(f.ExchangeId) || f.AppId != this.AppId);
                var localQueues = changedQueues.FindAll(f => f.AppId == this.AppId);
                var localBindings = changedBindings.FindAll(f => f.AppId == this.AppId);

                var timeout = this.heartbeatCycle * 2;
                removeKeys = this.heartbeats
                    .Where(f => f.Key != this.ServiceId && DateTime.UtcNow.Subtract(f.Value) > timeout)
                    .Select(f => f.Key).ToList();
                if (removeKeys.Count > 0)
                    removeKeys.ForEach(f => this.heartbeats.TryRemove(f, out _));

                //启动消费者
                var nodeIds = this.heartbeats.Keys.ToList();
                nodeIds.Sort((x, y) => x.CompareTo(y));
                var currentNodeIds = string.Join(",", nodeIds);
                if (currentNodeIds != this.lastNodeIds)
                    Console.WriteLine($"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}, 可用节点：{currentNodeIds}");

                int index = 0, nodeCount = nodeIds.Count;
                var localConsumers = new Dictionary<string, DispatchInfo>();

                //创建扩容的有状态队列和绑定，包括业务队列和转发队列
                var myQueues = localQueues.Where(f => f.IsStateful)
                    .OrderBy(f => f.QueueId).ToList();

                foreach (var myQueue in myQueues)
                {
                    if (myQueue.ChangeType != ChangeType.ScalingUp)
                        continue;

                    var myBindings = localBindings.FindAll(f => f.QueueId == myQueue.QueueId);
                    for (int i = myQueue.LastWorkloadTotal; i < myQueue.WorkloadTotal; i++)
                    {
                        var queueName = $"{myQueue.QueueId}.{i}";
                        if (this.isAllowCreateQueue)
                            await this.rabbitProducer.CreateQueue(queueName, myQueue.IsQuorumQueue, myQueue.IsSingleActiveConsumer, false);

                        //转发队列没有绑定数据
                        if (this.isAllowCreateBinding && myBindings.Count > 0)
                        {
                            foreach (var myBinding in myBindings)
                                await this.rabbitProducer.BindQueue(myBinding.ExchangeId, queueName, i.ToString());
                        }
                    }
                }

                var statefullQueues = localQueues.Where(f => f.IsStateful)
                    .OrderBy(f => f.QueueId).ToList();

                //分配有状态队列SAC激活消费者
                foreach (var myQueue in statefullQueues)
                {
                    string queueName = null;
                    var queueType = myQueue.IsTransfer ? QueueType.Transfer : QueueType.Message;
                    for (int i = 0; i < myQueue.WorkloadTotal; i++)
                    {
                        queueName = $"{myQueue.QueueId}.{i}";
                        this.Dispatch(queueType, index, i, queueName, myQueue, nodeIds, localConsumers);
                        index++;
                    }
                }

                //创建无状态队列消费者
                var statelessQueues = localQueues.Where(f => !f.IsStateful).OrderBy(f => f.QueueId).ToList();
                foreach (var myQueue in statelessQueues)
                {
                    var queueName = myQueue.QueueId;
                    for (int i = 0; i < myQueue.WorkloadTotal; i++)
                    {
                        this.Dispatch(QueueType.Message, index, 0, queueName, myQueue, nodeIds, localConsumers);
                        index++;
                    }
                }

                //创建有状态队列SAC等待消费者
                foreach (var myQueue in statefullQueues)
                {
                    var queueType = myQueue.IsTransfer ? QueueType.Transfer : QueueType.Message;
                    for (int i = 0; i < myQueue.WorkloadTotal; i++)
                    {
                        string queueName = null;
                        if (myQueue.IsTransfer && myQueue.WorkloadTotal == 1)
                            queueName = myQueue.QueueId;
                        else queueName = $"{myQueue.QueueId}.{i}";
                        for (int sacIndex = 1; sacIndex < this.sacCount; sacIndex++)
                        {
                            this.Dispatch(queueType, index, i, queueName, myQueue, nodeIds, localConsumers);
                            index++;
                        }
                    }
                }

                this.lastNodeIds = currentNodeIds;

                //根据分配好本pod中的消费者数量，创建新增消费者，并删除多余消费者
                var removeConsumers = new List<RabbitConsumer>();
                foreach (var queueName in localConsumers.Keys)
                {
                    var dispatchInfo = localConsumers[queueName];
                    var myQueue = dispatchInfo.Queue;
                    var consumerCount = dispatchInfo.ConsumerCount;
                    if (!this.consumers.TryGetValue(queueName, out var rabbitConsumers))
                        this.consumers.TryAdd(queueName, rabbitConsumers = new());

                    foreach (var myRabbitConsumer in rabbitConsumers)
                    {
                        if (!myRabbitConsumer.IsActivated || myRabbitConsumer.PrefetchCount != myQueue.PrefetchCount
                            || myRabbitConsumer.IsLogEnabled != myQueue.IsLogEnabled)
                        {
                            myRabbitConsumer.IsLogEnabled = myQueue.IsLogEnabled;
                            myRabbitConsumer.PrefetchCount = myQueue.PrefetchCount;
                            await myRabbitConsumer.ShutdownAsync();
                            await myRabbitConsumer.StartAsync();
                            Console.WriteLine($"消费者参数变更，{myRabbitConsumer.QueueName}, ConsumerId:{myRabbitConsumer.ConsumerId}已重启");
                        }
                    }
                    if (rabbitConsumers.Count < consumerCount)
                    {
                        for (int i = rabbitConsumers.Count; i < consumerCount; i++)
                            await this.CreateConsumer(dispatchInfo, rabbitConsumers);
                    }
                    else if (rabbitConsumers.Count > consumerCount)
                    {
                        var isScalingDown = myQueue.ChangeType == ChangeType.ScalingDown
                            && dispatchInfo.QueueIndex >= myQueue.WorkloadTotal;
                        for (int i = rabbitConsumers.Count - 1; i >= consumerCount; i--)
                        {
                            var rabbitConsumer = rabbitConsumers[i];
                            if (isScalingDown)
                            {
                                var timedConsumers = this.shutdownConsumers.GetOrAdd(queueName, f => new());
                                timedConsumers.Consumers.Add(rabbitConsumer);
                                timedConsumers.HasMessages = true;
                                Console.WriteLine($"缩容队列，多余消费者{rabbitConsumer.QueueName}, ConsumerId:{rabbitConsumer.ConsumerId}延迟关闭");
                            }
                            else
                            {
                                await rabbitConsumer.ShutdownAsync();
                                Console.WriteLine($"多余消费者{rabbitConsumer.QueueName}, ConsumerId:{rabbitConsumer.ConsumerId}已关闭");
                            }
                            removeConsumers.Add(rabbitConsumer);
                        }
                    }
                    if (removeConsumers.Count > 0)
                        removeConsumers.ForEach(f => rabbitConsumers.Remove(f));
                }

                //删除未分配在本pod中的多余消费者，一种是队列存在只是消费者迁移到其他pod中，另一种是缩容的队列
                removeKeys.Clear();
                foreach (var queueName in this.consumers.Keys)
                {
                    if (localConsumers.ContainsKey(queueName))
                        continue;
                    removeKeys.Add(queueName);

                    removeConsumers.Clear();
                    var rabbitConsumers = this.consumers[queueName];
                    if (rabbitConsumers == null || rabbitConsumers.Count == 0)
                        continue;

                    var queueId = rabbitConsumers[0].QueueId;
                    var myQueue = localQueues.Find(f => f.QueueId == queueId);
                    foreach (var rabbitConsumer in rabbitConsumers)
                    {
                        var isScalingDown = myQueue.ChangeType == ChangeType.ScalingDown
                            && rabbitConsumer.QueueIndex >= myQueue.WorkloadTotal;
                        if (isScalingDown)
                        {
                            var timedConsumers = this.shutdownConsumers.GetOrAdd(queueName, f => new());
                            timedConsumers.Consumers.Add(rabbitConsumer);
                            timedConsumers.HasMessages = true;
                            Console.WriteLine($"缩容队列，多余消费者{rabbitConsumer.QueueName}, ConsumerId:{rabbitConsumer.ConsumerId}延迟关闭");
                        }
                        else
                        {
                            await rabbitConsumer.ShutdownAsync();
                            Console.WriteLine($"多余消费者{rabbitConsumer.QueueName}, ConsumerId:{rabbitConsumer.ConsumerId}已关闭");
                        }
                        removeConsumers.Remove(rabbitConsumer);
                    }
                    if (removeConsumers.Count > 0)
                        removeConsumers.ForEach(f => rabbitConsumers.Remove(f));
                }
                if (removeKeys.Count > 0)
                    removeKeys.ForEach(f => this.consumers.TryRemove(f, out _));
            }

            if (this.isProducer)
            {
                //向发送消息循环，发送变更消息
                var statefulBindings = changedBindings.FindAll(f =>
                     f.IsStateful).ToDictionary(f => f.ExchangeId, f => f);
                var transferQueues = changedQueues.FindAll(f =>
                    f.IsTransfer).ToDictionary(f => f.AppId, f => f);
                await this.messageChannel.Writer.WriteAsync(new Message
                {
                    MessageId = ObjectId.NewId(),
                    Type = Consts.ChangeConsumers,
                    Body = (statefulBindings, transferQueues)
                });
            }
            removeKeys.Clear();
            //缩容的消费者，等待关闭
            foreach (var queueName in this.shutdownConsumers.Keys)
            {
                var timedConsumers = this.shutdownConsumers[queueName];
                var rabbitConsumer = timedConsumers.Consumers[0];
                var hasMessages = await rabbitConsumer.HasMessage();
                if (timedConsumers.HasMessages != hasMessages)
                {
                    timedConsumers.HasMessages = hasMessages;
                    if (!hasMessages) timedConsumers.UpdatedAt = DateTime.UtcNow;
                }
                if (hasMessages) continue;

                //队列排空1分钟后，删除消费者
                if (DateTime.UtcNow.Subtract(timedConsumers.UpdatedAt) < TimeSpan.FromMinutes(1))
                    continue;
                foreach (var myConsumer in timedConsumers.Consumers)
                    await myConsumer.ShutdownAsync();

                Console.WriteLine($"缩容队列{queueName}，所有消费者共{timedConsumers.Consumers.Count}个已关闭");
                timedConsumers.Consumers.Clear();
                removeKeys.Add(queueName);
            }
            if (removeKeys.Count > 0)
                removeKeys.ForEach(f => this.shutdownConsumers.TryRemove(f, out _));

            //转发队列更新为已确认
            var transferQueue = changedQueues.Find(f => f.AppId == this.AppId && f.IsTransfer);
            if (transferQueue != null) await this.repository.Confirm(transferQueue);

            this.queues = changedQueues;
            this.bindings = changedBindings;
        }
        finally
        {
            Interlocked.Exchange(ref this.isChangeConsumers, 0);
        }
    }
    private void Dispatch(QueueType queueType, int index, int queueIndex, string queueName,
        Queue myQueue, List<string> nodeIds, Dictionary<string, DispatchInfo> localConsumers)
    {
        var nodeId = nodeIds.Count > 1 ? nodeIds[index % nodeIds.Count] : this.ServiceId;
        if (nodeId != this.ServiceId) return;
        if (!localConsumers.TryGetValue(queueName, out var dispatchInfo))
        {
            localConsumers.TryAdd(queueName, dispatchInfo = new DispatchInfo
            {
                QueueType = queueType,
                ConsumerCount = 0
            });
        }
        dispatchInfo.Queue = myQueue;
        dispatchInfo.QueueIndex = queueIndex;
        dispatchInfo.QueueName = queueName;
        dispatchInfo.ConsumerCount++;
    }
    private async Task CreateConsumer(DispatchInfo dispatchInfo, List<RabbitConsumer> rabbitConsumers)
    {
        //新增消费者，直接启动
        var myQueue = dispatchInfo.Queue;
        var queueName = dispatchInfo.QueueName;
        if (this.shutdownConsumers.TryGetValue(queueName, out var timedConsumers))
        {
            if (timedConsumers.Consumers.Count > 0)
            {
                var rabbitConsumer = timedConsumers.Consumers[0];
                rabbitConsumer.IsLogEnabled = myQueue.IsLogEnabled;
                rabbitConsumers.Add(rabbitConsumer);
                timedConsumers.Consumers.RemoveAt(0);
            }
            if (timedConsumers.Consumers.Count == 0)
                this.shutdownConsumers.TryRemove(queueName, out _);
            return;
        }
        var queueId = myQueue.QueueId;
        //转发队列没有消费者处理程序，直接转发到目标队列
        this.consumerHandlers.TryGetValue(queueId, out var exchangeHandlers);
        var myRabbitConsumer = new RabbitConsumer(dispatchInfo,
            this, this.serviceProvider, myQueue.PrefetchCount, exchangeHandlers);
        myRabbitConsumer.IsLogEnabled = myQueue.IsLogEnabled;
        rabbitConsumers.Add(myRabbitConsumer);
        await myRabbitConsumer.StartAsync();
        var queueType = myQueue.IsStateful ? "有" : "无";
        var consumerId = $"{dispatchInfo.QueueName}.{this.ServiceId}";
        Console.WriteLine($"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}, {queueType}状态队列{dispatchInfo.QueueName} 消费者{consumerId}已启动，当前消费者数量：{rabbitConsumers.Count}");
    }
    private void EnsureAvailable()
    {
        if (!this.IsEnabled)
            throw new InvalidOperationException("MessageDriven is disabled.");
    }
    private async Task Delay(TimeSpan timeout)
    {
        try
        {
            await Task.Delay(timeout, this.stopTokenSource.Token);
        }
        catch (OperationCanceledException)
            when (!this.stopTokenSource.IsCancellationRequested)
        {
            // flush timeout
        }
    }
}