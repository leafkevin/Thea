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
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Thea.Logging;

namespace Thea.MessageDriven;

class MessageDrivenService : IMessageDriven, IHostedService
{
    private Task mainTask = Task.CompletedTask;
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
    private List<QueueTransfer> queueTransfers;

    private RabbitConsumer heartbeatConsumer;
    private RabbitConsumer rpcConsumer;
    private string lastNodeIds = null;
    private readonly int sacCount = 2;
    private readonly int messagePoolSize = 50000;

    private readonly Dictionary<string, Dictionary<string, MethodInfo>> consumerHandlers = new();
    private readonly IServiceProvider serviceProvider;
    private readonly IConfiguration configuration;
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
        this.configuration = serviceProvider.GetService<IConfiguration>();
        this.IsEnabled = this.configuration.GetValue("MessageDriven:IsEnabled", true);
        if (!this.IsEnabled) return;

        this.serviceProvider = serviceProvider;
        this.ServiceId = ObjectId.NewId();
        this.AppId = this.configuration.GetValue<string>("AppId");
        this.RpcQueue = $"{Consts.RpcExchange}.{this.AppId}.{this.ServiceId}";
        var endPoints = this.configuration.GetSection("MessageDriven:EndPoints").Get<string[]>();
        if (endPoints == null || endPoints.Length == 0)
            throw new Exception("未设置MessageDriven:EndPoints，无法初始化MessageDrivenService对象");
        this.tcpEndPoints = endPoints.Select(f => AmqpTcpEndpoint.Parse(f)).ToList();
        this.isAllowCreateQueue = this.configuration.GetValue("MessageDriven:IsAllowCreateQueue", true);
        this.isAllowCreateExchange = this.configuration.GetValue("MessageDriven:IsAllowCreateExchange", true);
        this.isAllowCreateBinding = this.configuration.GetValue("MessageDriven:IsAllowCreateBinding", true);
        this.heartbeatCycle = TimeSpan.FromSeconds(this.configuration.GetValue("MessageDriven:Heartbeat", 10));
        this.messagePoolSize = this.configuration.GetValue("MessageDriven:MessagePoolSize", 50000);
        if (string.IsNullOrEmpty(this.AppId))
        {
            this.logger.LogTagError("MessageDriven", "未设置AppId，无法初始化MessageDrivenService对象");
            throw new Exception("未设置AppId，无法初始化MessageDrivenService对象");
        }
        this.sacCount = this.configuration.GetValue("MessageDriven:SacCount", 2);
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
                    await Task.Delay(remaining, this.stopTokenSource.Token);
                }
            }
            catch (OperationCanceledException)
                when (this.stopTokenSource.IsCancellationRequested)
            {
                // 日志读取和延时等待都可能因服务停止而取消，正常退出循环。
                break;
            }
            catch (Exception ex)
            {
                var exception = ex.InnerException ?? ex;
                this.logger.LogTagError("MessageDriven", exception, $"ExecuteAsync is failled");
                Console.WriteLine($"MessageDriven，,ExecuteAsync is failled，{ex}");
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
            // 本次日志读取超时，返回主循环刷新日志；服务停止则交给外层处理。
        }
        return false;
    }
    public async Task ProducerLoopAsync()
    {
        var routings = this.queues.Join(this.bindings, queue => queue.QueueId,
            binding => binding.QueueId, (queue, binding) => new ProducerRouting
            {
                ExchangeId = binding.ExchangeId,
                IsStateful = queue.IsStateful,
                WorkloadTotal = queue.IsStateful ? Math.Min(queue.WorkloadTotal, queue.ChangingWorkloadTotal) : 0
            })
            .DistinctBy(f => f.ExchangeId).ToDictionary(f => f.ExchangeId, f => f);
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
                                if (routings.TryGetValue(message.Exchange, out var myRouting))
                                {
                                    var routingKey = myRouting.IsStateful ? JumpConsistentHash.GetBucket(
                                        message.RoutingKey, myRouting.WorkloadTotal).ToString() : message.RoutingKey;
                                    await this.rabbitProducer.PublishAsync(message.Exchange, routingKey, properties, jsonMessage);
                                }
                                message.Waiter?.TrySetResult(true);
                                break;
                            case Consts.ChangeConsumers:
                                //本地pod消费者变更通知已更新，相应的队列和绑定都已创建，其他的pod不同步也没有关系
                                routings = message.Body as Dictionary<string, ProducerRouting>;
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
            try { Interlocked.CompareExchange(ref this.appState, targetSate, 1); }
            finally { this.lifecycleLocker.Release(); }
        }
    }
    private async Task CompleteStopAsync()
    {
        if (this.startingTask != null)
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
        if (this.queueTransfers != null && this.queueTransfers.Count > 0)
            consumers = consumers.Concat(this.queueTransfers.SelectMany(f => f.Consumers)).ToList();
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
        if (this.queueTransfers != null && this.queueTransfers.Count > 0)
        {
            foreach (var transfer in this.queueTransfers)
                transfer.Consumers.Clear();
        }
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
    public void UseMigrating(string queueName, bool isSingleActiveConsumer)
    {
        this.queueTransfers ??= new();
        if (this.queueTransfers.Exists(f => f.FromQueue == queueName))
            return;
        this.queueTransfers.Add(new QueueTransfer
        {
            FromQueue = queueName,
            IsSingleActiveConsumer = isSingleActiveConsumer,
            Consumers = new()
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
                ChangingWorkloadTotal = 2,
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
            myQueue.ChangingWorkloadTotal = 2;
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
                ChangingWorkloadTotal = 2,
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
                ChangingWorkloadTotal = 2,
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
        myQueue.ChangingWorkloadTotal = workloadTotal;
        if (prefetchCount.HasValue)
            myQueue.PrefetchCount = prefetchCount.Value;
        if (isLogEnabled.HasValue)
            myQueue.IsLogEnabled = isLogEnabled.Value;
        await this.repository.ChangeSharding(myQueue);
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

        foreach (var myQueue in this.queues)
        {
            var dbQueue = dbQueues.Find(f => f.QueueId == myQueue.QueueId);
            dbQueue.IsStateful = myQueue.IsStateful;
            dbQueue.IsQuorumQueue = myQueue.IsQuorumQueue;
            dbQueue.IsSingleActiveConsumer = myQueue.IsSingleActiveConsumer;
            //以数据库配置为主
            dbQueue.ChangeType = ChangeType.None;
        }

        //数据库中本应用配置可能会多于本地配置，本应用的配置以本地为主，其他应用以数据库为主
        var myQueues = dbQueues.FindAll(f => this.localQueueIds.Contains(f.QueueId));
        var myBindings = dbBindings.FindAll(f => this.localExchangeIds.Contains(f.ExchangeId));

        //一个交换机代表一块业务
        //一个交换机只能绑定一个有状态队列，但可以同时绑定多个无状态队列
        //一个交换机不能同时绑定有状态队列和无状态队列，只能选择其一绑定
        //允许多个交换机绑定到同一个有状态队列
        var exchangeQueues = myBindings.Where(f => f.IsStateful)
            .GroupBy(f => f.ExchangeId).ToDictionary(f => f.Key, f => f.Select(t => t.QueueId).ToList())
            .Where(f => f.Value.Count > 1).ToList();
        if (exchangeQueues.Count > 0)
        {
            foreach (var exchangeQueue in exchangeQueues)
                throw new Exception($"与交换机{exchangeQueue.Key}绑定的有状态队列只能一个，目前绑定的有状态队列[{string.Join(',', exchangeQueue.Value)}]");
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
        string queueName = null;
        if (this.isAllowCreateQueue)
        {
            //创建工作负载队列和转发队列
            foreach (var myQueue in myQueues)
            {
                if (myQueue.IsStateful)
                {
                    //刚启动不确定缩容场景消息是否排空完毕，先按最大分片创建队列
                    var workloadTotal = Math.Max(myQueue.WorkloadTotal, myQueue.ChangingWorkloadTotal);
                    for (int i = 0; i < workloadTotal; i++)
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
            foreach (var myQueue in myQueues)
            {
                var myQueueBindings = myBindings.FindAll(f => f.QueueId == myQueue.QueueId);
                if (myQueue.IsStateful)
                {
                    //刚启动不确定缩容场景消息是否排空完毕，先按最大分片创建交换机绑定
                    var workloadTotal = Math.Max(myQueue.WorkloadTotal, myQueue.ChangingWorkloadTotal);
                    foreach (var myBinding in myQueueBindings)
                    {
                        for (int i = 0; i < workloadTotal; i++)
                        {
                            queueName = $"{myQueue.QueueId}.{i}";
                            await this.rabbitProducer.BindQueue(myBinding.ExchangeId, queueName, i.ToString());
                        }
                    }
                }
                else
                {
                    foreach (var myBinding in myQueueBindings)
                    {
                        await this.rabbitProducer.BindQueue(myBinding.ExchangeId, myBinding.QueueId, Consts.FanoutRoutingKey);
                    }
                }
            }
        }

        //迁移已有旧队列消息
        if (this.queueTransfers != null && this.queueTransfers.Count > 0)
        {
            foreach (var queueTransfer in this.queueTransfers)
            {
                var consumerCount = queueTransfer.IsSingleActiveConsumer ? this.sacCount : 1;
                var dispatchInfo = new DispatchInfo
                {
                    QueueType = QueueType.Transfer,
                    Queue = new Queue
                    {
                        QueueId = queueTransfer.FromQueue,
                        PrefetchCount = 250
                    },
                    QueueName = queueTransfer.FromQueue,
                    QueueIndex = 0,
                    ConsumerCount = consumerCount
                };
                for (int i = 0; i < consumerCount; i++)
                    await this.CreateConsumer(dispatchInfo, queueTransfer.Consumers);
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
                dbQueue.IsQuorumQueue = myQueue.IsQuorumQueue;
                dbQueue.IsSingleActiveConsumer = myQueue.IsSingleActiveConsumer;

                //确定变更类型，扩容/缩容               
                if (dbQueue.ChangingWorkloadTotal == myQueue.WorkloadTotal)
                    dbQueue.ChangeType = ChangeType.None;
                else if (dbQueue.ChangingWorkloadTotal > myQueue.WorkloadTotal)
                    dbQueue.ChangeType = ChangeType.ScalingUp;
                else dbQueue.ChangeType = ChangeType.ScalingDown;
            }
            var changedQueues = dbQueues;
            var changedBindings = dbBindings;
            var removeKeys = new List<string>();
            var confirmQueues = new List<Queue>();

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
                    for (int i = myQueue.WorkloadTotal; i < myQueue.ChangingWorkloadTotal; i++)
                    {
                        var queueName = $"{myQueue.QueueId}.{i}";
                        if (this.isAllowCreateQueue)
                            await this.rabbitProducer.CreateQueue(queueName, myQueue.IsQuorumQueue, myQueue.IsSingleActiveConsumer, false);

                        if (this.isAllowCreateBinding && myBindings.Count > 0)
                        {
                            foreach (var myBinding in myBindings)
                                await this.rabbitProducer.BindQueue(myBinding.ExchangeId, queueName, i.ToString());
                        }
                    }
                    //扩容的队列，消费者创建完毕，就可以确认了
                    confirmQueues.Add(myQueue);
                }

                var statefullQueues = localQueues.Where(f => f.IsStateful)
                    .OrderBy(f => f.QueueId).ToList();

                //分配有状态队列SAC激活消费者
                foreach (var myQueue in statefullQueues)
                {
                    //缩容期间重启，以缩容前分片数创建消费者
                    var workloadTotal = isFirst ? Math.Max(myQueue.WorkloadTotal,
                        myQueue.ChangingWorkloadTotal) : myQueue.ChangingWorkloadTotal;
                    for (int i = 0; i < workloadTotal; i++)
                    {
                        var queueName = $"{myQueue.QueueId}.{i}";
                        this.Dispatch(QueueType.Message, index, i, queueName, myQueue, nodeIds, localConsumers);
                        index++;
                    }
                }

                //创建无状态队列消费者
                var statelessQueues = localQueues.Where(f => !f.IsStateful)
                    .OrderBy(f => f.QueueId).ToList();
                foreach (var myQueue in statelessQueues)
                {
                    var queueName = myQueue.QueueId;
                    for (int i = 0; i < myQueue.ChangingWorkloadTotal; i++)
                    {
                        this.Dispatch(QueueType.Message, index, 0, queueName, myQueue, nodeIds, localConsumers);
                        index++;
                    }
                    if (myQueue.WorkloadTotal != myQueue.ChangingWorkloadTotal)
                        confirmQueues.Add(myQueue);
                }

                //创建有状态队列SAC等待消费者
                foreach (var myQueue in statefullQueues)
                {
                    //缩容期间重启，以缩容前分片数创建消费者
                    var workloadTotal = isFirst ? Math.Max(myQueue.WorkloadTotal,
                        myQueue.ChangingWorkloadTotal) : myQueue.ChangingWorkloadTotal;
                    for (int i = 0; i < workloadTotal; i++)
                    {
                        var queueName = $"{myQueue.QueueId}.{i}";
                        for (int sacIndex = 1; sacIndex < this.sacCount; sacIndex++)
                        {
                            this.Dispatch(QueueType.Message, index, i, queueName, myQueue, nodeIds, localConsumers);
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
                            && dispatchInfo.QueueIndex >= myQueue.ChangingWorkloadTotal;
                        for (int i = rabbitConsumers.Count - 1; i >= consumerCount; i--)
                        {
                            var rabbitConsumer = rabbitConsumers[i];
                            if (isScalingDown && myQueue.IsStateful)
                            {
                                var timedConsumers = this.shutdownConsumers.GetOrAdd(queueName, f => new(myQueue.QueueId));
                                timedConsumers.Consumers.Add(rabbitConsumer);
                                rabbitConsumer.Queue = myQueue;
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
                            && rabbitConsumer.QueueIndex >= myQueue.ChangingWorkloadTotal;
                        if (isScalingDown && myQueue.IsStateful)
                        {
                            var timedConsumers = this.shutdownConsumers.GetOrAdd(queueName, f => new(queueId));
                            timedConsumers.Consumers.Add(rabbitConsumer);
                            rabbitConsumer.Queue = myQueue;
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

            removeKeys.Clear();
            //缩容的消费者，等待关闭
            foreach (var queueName in this.shutdownConsumers.Keys)
            {
                Queue myQueue = null;
                var timedConsumers = this.shutdownConsumers[queueName];
                if (timedConsumers.Consumers == null || timedConsumers.Consumers.Count == 0)
                {
                    //缩容队列已经排空，上次变更路由失败，本次继续变更路由
                    myQueue = changedQueues.Find(f => f.QueueId == timedConsumers.QueueId);
                    if (await this.repository.ChangeRouting(myQueue))
                        removeKeys.Add(queueName);
                    continue;
                }
                (var isSuccess, var hasMessages) = await this.RequestApi(queueName);
                if (!isSuccess) hasMessages = await timedConsumers.Consumers[0].HasMessage();
                if (timedConsumers.HasMessages != hasMessages)
                    timedConsumers.HasMessages = hasMessages;
                if (!hasMessages) timedConsumers.ConfirmTimes++;
                else timedConsumers.ConfirmTimes = 0;
                if (hasMessages || timedConsumers.ConfirmTimes < 2)
                {
                    timedConsumers.UpdatedAt = DateTime.UtcNow;
                    continue;
                }

                //队列排空1分钟后，删除消费者
                var timeout = TimeSpan.FromMinutes(1);
                if (timeout < this.heartbeatCycle * 4)
                    timeout = this.heartbeatCycle * 4;
                if (DateTime.UtcNow.Subtract(timedConsumers.UpdatedAt) < timeout)
                    continue;

                foreach (var myConsumer in timedConsumers.Consumers)
                    await myConsumer.ShutdownAsync();
                Console.WriteLine($"缩容队列{queueName}，所有消费者共{timedConsumers.Consumers.Count}个已关闭");
                timedConsumers.Consumers.Clear();

                //缩容队列已经排空，取最新的队列分片变更路由
                myQueue = changedQueues.Find(f => f.QueueId == timedConsumers.QueueId);
                if (await this.repository.ChangeRouting(myQueue))
                    removeKeys.Add(queueName);
            }
            if (removeKeys.Count > 0)
                removeKeys.ForEach(f => this.shutdownConsumers.TryRemove(f, out _));

            if (confirmQueues.Count > 0)
            {
                confirmQueues = confirmQueues.DistinctBy(f => f.QueueId).ToList();
                foreach (var confirmQueue in confirmQueues)
                    await this.repository.ChangeRouting(confirmQueue);
            }

            if (this.isProducer)
            {
                //向发送消息循环，发送变更消息
                var routings = changedQueues.Join(changedBindings, queue => queue.QueueId,
                    binding => binding.QueueId, (queue, binding) => new ProducerRouting
                    {
                        ExchangeId = binding.ExchangeId,
                        IsStateful = queue.IsStateful,
                        WorkloadTotal = queue.IsStateful ? Math.Min(queue.WorkloadTotal, queue.ChangingWorkloadTotal) : 0
                    })
                    .DistinctBy(f => f.ExchangeId).ToDictionary(f => f.ExchangeId, f => f);
                await this.messageChannel.Writer.WriteAsync(new Message
                {
                    MessageId = ObjectId.NewId(),
                    Type = Consts.ChangeConsumers,
                    Body = routings
                });
            }

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
        var queueType = myQueue.IsStateful ? "有" : "无";
        var consumerId = $"{dispatchInfo.QueueName}.{this.ServiceId}";
        if (this.shutdownConsumers.TryGetValue(queueName, out var timedConsumers))
        {
            if (timedConsumers.Consumers != null && timedConsumers.Consumers.Count > 0)
            {
                var rabbitConsumer = timedConsumers.Consumers[0];
                rabbitConsumers.Add(rabbitConsumer);
                timedConsumers.Consumers.RemoveAt(0);
                if (!rabbitConsumer.IsActivated || rabbitConsumer.PrefetchCount != myQueue.PrefetchCount
                    || rabbitConsumer.IsLogEnabled != myQueue.IsLogEnabled)
                {
                    rabbitConsumer.PrefetchCount = myQueue.PrefetchCount;
                    rabbitConsumer.IsLogEnabled = myQueue.IsLogEnabled;
                    await rabbitConsumer.ShutdownAsync();
                    await rabbitConsumer.StartAsync();
                }
                if (timedConsumers.Consumers.Count == 0)
                    this.shutdownConsumers.TryRemove(queueName, out _);
                Console.WriteLine($"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}, 重用{queueType}状态队列{dispatchInfo.QueueName} 消费者{consumerId}，当前消费者数量：{rabbitConsumers.Count}");
                return;
            }
            //上次路由变更失败，本次又扩容了，不需要变更了，直接删除
            else this.shutdownConsumers.TryRemove(queueName, out _);
        }
        var queueId = myQueue.QueueId;
        //转发队列没有消费者处理程序，直接转发到目标队列
        this.consumerHandlers.TryGetValue(queueId, out var exchangeHandlers);
        var myRabbitConsumer = new RabbitConsumer(dispatchInfo,
            this, this.serviceProvider, myQueue.PrefetchCount, exchangeHandlers);
        myRabbitConsumer.IsLogEnabled = myQueue.IsLogEnabled;
        rabbitConsumers.Add(myRabbitConsumer);
        await myRabbitConsumer.StartAsync();
        Console.WriteLine($"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}, {queueType}状态队列{dispatchInfo.QueueName} 消费者{consumerId}已启动，当前消费者数量：{rabbitConsumers.Count}");
    }
    private void EnsureAvailable()
    {
        if (!this.IsEnabled)
            throw new InvalidOperationException("MessageDriven is disabled.");
    }
    private async Task<(bool, bool)> RequestApi(string queueName)
    {
        string requestUrl = null;
        try
        {
            var managementPort = this.configuration.GetValue("MessageDriven:ManagementPort", 15672);
            var userName = this.configuration.GetValue("MessageDriven:User", "admin");
            var password = this.configuration.GetValue("MessageDriven:Password", "123456");
            var clientFactory = this.serviceProvider.GetRequiredService<IHttpClientFactory>();
            using var client = clientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(10);
            // "/" 编码后为 "%2F"，队列名也需要编码
            var virtualHost = "/";
            var parameters = $"{Uri.EscapeDataString(virtualHost)}/{Uri.EscapeDataString(queueName)}";
            requestUrl = $"http://{this.tcpEndPoints[0].HostName}:{managementPort}/api/queues/{parameters}";
            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{userName}:{password}"));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            using var response = await client.GetAsync(requestUrl);
            if (response.StatusCode == HttpStatusCode.NotFound)
                return (false, false);
            //throw new HttpRequestException($"队列不存在或当前用户不可见：{virtualHost}/{queueName}", null, response.StatusCode);
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<RabbitQueueInfo>();
            if (result == null || result.MessagesReady is null || result.MessagesUnacknowledged is null)
                //throw new InvalidOperationException("RabbitMQ 队列统计暂不可用");
                return (false, false);
            return (true, result.MessagesReady > 0 || result.MessagesUnacknowledged > 0);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"请求Management Api{requestUrl}失败，{ex}");
        }
        return (false, false);
    }
}