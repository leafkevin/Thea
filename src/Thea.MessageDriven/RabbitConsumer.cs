using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Thea.Json;
using Thea.Logging;

namespace Thea.MessageDriven;

class RabbitConsumer
{
    private readonly SemaphoreSlim lifecycleLocker = new(1, 1);
    private int workers = 0;
    private readonly MessageDrivenService parent;
    private readonly ILogger<RabbitConsumer> logger;
    private readonly Dictionary<string, Func<string, Task<string>>> exchangeHandlers;
    private ConnectionFactory factory;
    private IConnection connection;
    private IChannel channel;
    private AsyncEventingBasicConsumer consumer;
    private string consumerTag;
    //0:未启动, 1:运行中, 2:关闭中
    private int state = 0;
    private TaskCompletionSource<bool> workersWaiter;

    private DispatchInfo dispatchInfo;
    public volatile int PrefetchCount;
    public volatile bool IsLogEnabled;

    private QueueType queueType => this.dispatchInfo.QueueType;
    public string QueueId => this.dispatchInfo.Queue.QueueId;
    public int QueueIndex => this.dispatchInfo.QueueIndex;
    public string QueueName => this.dispatchInfo.QueueName;
    public string ConsumerId { get; private set; }
    public bool IsActivated => Volatile.Read(ref this.state) == 1 && (this.connection?.IsOpen ?? false)
        && (this.channel?.IsOpen ?? false) && (this.consumer?.IsRunning ?? false);
    public bool IsBusying => Volatile.Read(ref this.workers) > 0;

    public RabbitConsumer(DispatchInfo dispatchInfo, MessageDrivenService parent, IServiceProvider serviceProvider, int prefetchCount = 250, Dictionary<string, MethodInfo> exchangeMethodInfos = null)
    {
        this.parent = parent;
        this.dispatchInfo = dispatchInfo;
        this.ConsumerId = ObjectId.NewId();
        this.PrefetchCount = prefetchCount;
        this.logger = serviceProvider.GetService<ILogger<RabbitConsumer>>();
        var configuration = serviceProvider.GetService<IConfiguration>();
        var user = configuration.GetValue<string>("MessageDriven:User");
        var password = configuration.GetValue<string>("MessageDriven:Password");
        if (exchangeMethodInfos != null)
        {
            this.exchangeHandlers = new();
            foreach (var exchange in exchangeMethodInfos.Keys)
            {
                var consumerHandler = this.CreateConsumerHandler(serviceProvider, exchangeMethodInfos[exchange]);
                this.exchangeHandlers.TryAdd(exchange, consumerHandler);
            }
        }

        this.factory = new ConnectionFactory
        {
            UserName = user,
            Password = password,
            AutomaticRecoveryEnabled = true,
            ConsumerDispatchConcurrency = 1,
            RequestedHeartbeat = TimeSpan.FromSeconds(10),
            NetworkRecoveryInterval = TimeSpan.FromSeconds(5),
            //当队列很多，消息也很多时，quorum queue选举时间会变长，默认：20s
            ContinuationTimeout = TimeSpan.FromSeconds(120),
            ClientProperties = new Dictionary<string, object>()
            {
                { "connection_name", this.QueueName},
                { "client_api", "Thea.MessageDriven" }
            }
        };
    }
    public async Task StartAsync()
    {
        await this.lifecycleLocker.WaitAsync();
        int originalState = 0;
        try
        {
            originalState = Interlocked.CompareExchange(ref this.state, 1, 0);
            if (originalState != 0) return;
        }
        finally { this.lifecycleLocker.Release(); }
        try
        {
            var workerCount = Volatile.Read(ref this.workers);
            if (Volatile.Read(ref this.workers) != 0)
                throw new InvalidOperationException($"消费者启动失败，仍有{workerCount}个Worker未结束，ConsumerId: {this.ConsumerId}, QueueName: {this.QueueName}");

            Interlocked.Exchange(ref this.workers, 0);
            this.workersWaiter = new(TaskCreationOptions.RunContinuationsAsynchronously);
            this.connection = await this.factory.CreateConnectionAsync(this.parent.tcpEndPoints, this.ConsumerId);
            this.channel = await this.connection.CreateChannelAsync();
            await this.channel.BasicQosAsync(0, (ushort)this.PrefetchCount, false);
            switch (this.queueType)
            {
                case QueueType.Heartbeat:
                    await this.channel.QueueDeclareAsync(this.QueueName, true, true, false);
                    await this.channel.QueueBindAsync(this.QueueName, Consts.HeartbeatExchange, this.parent.AppId);
                    await this.BindHeartbeatHandler();
                    break;
                case QueueType.Message: await this.BindUserMessageHandler(); break;
                case QueueType.Transfer: await this.BindTransferHandler(); break;
                case QueueType.RpcResult:
                    await this.channel.QueueDeclareAsync(this.QueueName, false, true, false);
                    await this.channel.QueueBindAsync(this.QueueName, Consts.RpcExchange, this.parent.ServiceId);
                    await this.BindRpcResultHandler();
                    break;
            }
        }
        catch
        {
            Interlocked.Exchange(ref this.state, originalState);
            await this.DisposeAsync();
            throw;
        }
    }
    public async Task ShutdownAsync()
    {
        Task workersTask = null;
        await this.lifecycleLocker.WaitAsync();
        try
        {
            if (Interlocked.CompareExchange(ref this.state, 2, 1) != 1) return;
            workersTask = Volatile.Read(ref this.workers) > 0
                ? this.workersWaiter.Task : Task.CompletedTask;
        }
        finally { this.lifecycleLocker.Release(); }
        try
        {
            using var timeoutSource = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            //先停止投递
            if (this.channel?.IsOpen == true && !string.IsNullOrEmpty(this.consumerTag))
                await this.channel.BasicCancelAsync(this.consumerTag, cancellationToken: timeoutSource.Token);

            //等待正在处理的回调完成
            await workersTask.WaitAsync(timeoutSource.Token);

            //释放资源
            await this.DisposeAsync();
            Interlocked.Exchange(ref this.state, 0);
        }
        catch (OperationCanceledException)
        {
            await this.DisposeAsync();
            Interlocked.Exchange(ref this.state, 0);
            throw;
        }
        catch (Exception ex)
        {
            this.logger.LogTagError("RabbitConsumer", ex, $"关闭消费者失败，QueueName: {this.QueueName}, ConsumerId: {this.ConsumerId}, Workers: {Volatile.Read(ref this.workers)}");
            throw;
        }
    }
    public async Task<bool> HasMessage()
    {
        if (Volatile.Read(ref this.workers) > 0)
            return true;
        if (await this.MessageCount() > 0)
            return true;
        return false;
    }
    public async Task<uint> MessageCount()
    {
        if (!(this.channel?.IsOpen ?? false)) return 0;
        try
        {
            return await this.channel.MessageCountAsync(this.QueueName);
        }
        catch (AlreadyClosedException)
        {
            return 0;
        }
        catch (ObjectDisposedException)
        {
            return 0;
        }
    }
    private async Task DisposeAsync()
    {
        try
        {
            if (this.channel != null)
                await this.channel.DisposeAsync();
        }
        catch (Exception ex)
        {
            this.logger.LogTagError("RabbitConsumer", ex, $"DisposeAsync，关闭Channel失败, ConsumerId: {this.ConsumerId}, QueueName: {this.QueueName}");
        }
        try
        {
            if (this.connection != null)
                await this.connection.DisposeAsync();
        }
        catch (Exception ex)
        {
            this.logger.LogTagError("RabbitConsumer", ex, $"DisposeAsync，关闭Connection失败, ConsumerId: {this.ConsumerId}, QueueName: {this.QueueName}");
        }
        this.channel = null;
        this.connection = null;
        this.consumer = null;
        this.consumerTag = null;
    }
    private async Task SafeNack(IChannel rabbitChannel, ulong deliveryTag)
    {
        try
        {
            if (rabbitChannel?.IsOpen ?? false)
                await rabbitChannel.BasicNackAsync(deliveryTag, false, true);
        }
        catch (Exception ex)
        {
            this.logger.LogTagError("RabbitConsumer", ex, $"SafeNack，拒绝消息失败, ConsumerId: {this.ConsumerId}, QueueName: {this.QueueName}, DeliveryTag: {deliveryTag}");
        }
    }
    private async Task BindUserMessageHandler()
    {
        this.consumer = new AsyncEventingBasicConsumer(this.channel);
        //用户消息队列
        this.consumer.ReceivedAsync += async (model, ea) =>
        {
            if (!await this.TryEnterAsync())
                return;

            IDisposable scopeObj = null;
            string traceId = null;
            string jsonBody = null;
            var messageId = ea.BasicProperties.MessageId;
            var messageType = ea.BasicProperties.Type;
            try
            {
                var iLoop = 0;
                Exception exception = null;
                bool isSuccess = true;
                jsonBody = Encoding.UTF8.GetString(ea.Body.Span);

                //内部消息，交给消息总分发处处理
                string result = null;
                var createdAt = DateTime.UtcNow;
                var headers = ea.BasicProperties.Headers;
                if (headers != null && headers.TryGetValue("TraceId", out var objValue))
                    traceId = Encoding.UTF8.GetString((byte[])headers["TraceId"]);
                switch (messageType)
                {
                    case Consts.UserMessage:
                    case Consts.RpcMessage:
                        {
                            //赋值TraceId，用于日志跟踪                        
                            if (!string.IsNullOrEmpty(traceId))
                                scopeObj = this.logger.BeginScope(new LogEntity { TraceId = traceId });

                            while (iLoop < 3)
                            {
                                try
                                {
                                    var typedConsumerHandler = this.exchangeHandlers[ea.Exchange];
                                    result = await typedConsumerHandler.Invoke(jsonBody);
                                    isSuccess = true;
                                    break;
                                }
                                catch (Exception ex)
                                {
                                    isSuccess = false;
                                    exception = ex.InnerException ?? ex;
                                }
                                iLoop++;
                            }
                            if (!isSuccess) result = exception.ToString();
                            var logId = ObjectId.NewId();

                            if (this.IsLogEnabled || !isSuccess)
                            {
                                await this.parent.ProcessMessage(new Message
                                {
                                    MessageId = messageId,
                                    Type = Consts.Logs,
                                    TraceId = traceId,
                                    Body = new ExecLog
                                    {
                                        LogId = logId,
                                        TraceId = traceId,
                                        Exchange = ea.Exchange,
                                        RoutingKey = ea.RoutingKey,
                                        Queue = this.QueueName,
                                        Body = jsonBody,
                                        IsSuccess = isSuccess,
                                        Result = result,
                                        RetryTimes = iLoop,
                                        UpdatedBy = this.parent.AppId,
                                        UpdatedAt = DateTime.UtcNow
                                    }
                                });
                            }
                            var hasScope = ScopeLogger.TryGetScope(out var lastScopeState);
                            if (!hasScope || hasScope && lastScopeState.IsEnabled)
                            {
                                var resultBody = isSuccess ? "success" : "failed";
                                this.logger.LogEntity(new LogEntity
                                {
                                    Id = logId,
                                    ApiType = (int)ApiType.LocalInvoke,
                                    TraceId = traceId,
                                    Tag = "RabbitConsumer",
                                    Body = $"consumed {resultBody}, queue: {this.QueueName}, exchange: {ea.Exchange}, routingKey: {ea.RoutingKey}",
                                    LogLevel = (int)(isSuccess ? LogLevel.Information : LogLevel.Error),
                                    Exception = exception,
                                    Request = jsonBody,
                                    Response = result,
                                    Elapsed = (int)DateTime.UtcNow.Subtract(createdAt).TotalMilliseconds
                                });
                            }
                            if (messageType == Consts.RpcMessage)
                            {
                                //Console.WriteLine($"RpcMessage, Publish Response, MessageId: {message.MessageId}, ReplyTo:{replyToQueue}, DateTime: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}");
                                var replyToQueue = ea.BasicProperties.ReplyTo;
                                var rpcMessageType = isSuccess ? Consts.RpcResponse : Consts.RpcFailure;
                                var theaMessage = new Message
                                {
                                    MessageId = messageId,
                                    Type = rpcMessageType,
                                    Exchange = Consts.RpcExchange,
                                    RoutingKey = replyToQueue,
                                    ReplyTo = ea.BasicProperties.ReplyTo,
                                    Body = result,
                                    TraceId = traceId,
                                    Waiter = new(TaskCreationOptions.RunContinuationsAsynchronously)
                                };
                                await this.parent.ProcessMessage(theaMessage);
                                await theaMessage.Waiter.Task;
                            }
                            //RPC消息直接跳过，因为异常已经返回到前端了
                            if (!isSuccess && messageType == Consts.UserMessage)
                                throw exception;
                        }
                        break;
                }
                await this.channel.BasicAckAsync(ea.DeliveryTag, false);
            }
            catch (Exception ex)
            {
                var messageJson = new Message
                {
                    MessageId = ea.BasicProperties.MessageId,
                    Type = ea.BasicProperties.Type,
                    TraceId = traceId,
                    Exchange = ea.Exchange,
                    RoutingKey = ea.RoutingKey,
                    ReplyTo = ea.BasicProperties.ReplyTo,
                    Body = jsonBody
                }.ToJson();
                this.logger.LogTagError("RabbitConsumer", ex, $"BindUserMessageHandler，处理用户消息异常, ConsumerId: {this.ConsumerId}, Message: {messageJson}");
                Console.WriteLine($"处理用户消息异常, Message: {messageJson}, Exception: {ex}");
                await this.SafeNack(this.channel, ea.DeliveryTag);
            }
            finally
            {
                scopeObj?.Dispose();
                await this.ExitAsync();
            }
        };
        this.consumerTag = await this.channel.BasicConsumeAsync(this.QueueName, false, this.consumer);
    }
    private async Task BindHeartbeatHandler()
    {
        this.consumer = new AsyncEventingBasicConsumer(this.channel);
        this.consumer.ReceivedAsync += async (model, ea) =>
        {
            if (!await this.TryEnterAsync())
                return;

            try
            {
                var body = Encoding.UTF8.GetString(ea.Body.Span);
                if (ea.BasicProperties.AppId == this.parent.AppId)
                {
                    await this.parent.ProcessMessage(new Message
                    {
                        MessageId = ea.BasicProperties.MessageId,
                        //Heartbeat消息类型
                        Type = ea.BasicProperties.Type,
                        //服务ID
                        Body = body
                    });
                }
                await this.channel.BasicAckAsync(ea.DeliveryTag, false);
            }
            catch (Exception ex)
            {
                this.logger.LogTagError("RabbitConsumer", ex, $"BindHeartbeatHandler，处理心跳消息失败, ConsumerId: {this.ConsumerId}");
                await this.SafeNack(this.channel, ea.DeliveryTag);
            }
            finally
            {
                await this.ExitAsync();
            }
        };
        this.consumerTag = await this.channel.BasicConsumeAsync(this.QueueName, false, this.consumer);
    }
    private async Task BindRpcResultHandler()
    {
        this.consumer = new AsyncEventingBasicConsumer(this.channel);
        this.consumer.ReceivedAsync += async (model, ea) =>
        {
            if (!await this.TryEnterAsync())
                return;

            try
            {
                var result = Encoding.UTF8.GetString(ea.Body.Span);
                //Console.WriteLine($"RpcResponse, MessageId: {message.MessageId}, ReplyTo:{ea.BasicProperties.ReplyTo}, RoutingKey:{ea.RoutingKey}, DateTime: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}");
                await this.parent.ProcessMessage(new Message
                {
                    MessageId = ea.BasicProperties.MessageId,
                    Type = ea.BasicProperties.Type,
                    Body = result
                });
                await this.channel.BasicAckAsync(ea.DeliveryTag, false);
            }
            catch (Exception ex)
            {
                this.logger.LogTagError("RabbitConsumer", ex, $"BindRpcResultHandler，处理RPC结果失败, ConsumerId: {this.ConsumerId}");
                await this.SafeNack(this.channel, ea.DeliveryTag);
            }
            finally
            {
                await this.ExitAsync();
            }
        };
        this.consumerTag = await this.channel.BasicConsumeAsync(this.QueueName, false, this.consumer);
    }
    private async Task BindTransferHandler()
    {
        this.consumer = new AsyncEventingBasicConsumer(this.channel);
        this.consumer.ReceivedAsync += async (model, ea) =>
        {
            if (!await this.TryEnterAsync())
                return;
            try
            {
                var jsonBody = Encoding.UTF8.GetString(ea.Body.Span);
                var headers = ea.BasicProperties.Headers;
                string exchange = null, routingKey = null, traceId = null;
                if (headers.TryGetValue("Exchange", out var exchangeBytes))
                    exchange = Encoding.UTF8.GetString((byte[])exchangeBytes);
                if (headers.TryGetValue("RoutingKey", out var routingKeyBytes))
                    routingKey = Encoding.UTF8.GetString((byte[])routingKeyBytes);
                if (headers.TryGetValue("TraceId", out var traceIdBytes))
                    traceId = Encoding.UTF8.GetString((byte[])traceIdBytes);

                await this.parent.ProcessMessage(new Message
                {
                    MessageId = ea.BasicProperties.MessageId,
                    Type = ea.BasicProperties.Type,
                    Exchange = exchange,
                    RoutingKey = routingKey,
                    TraceId = traceId,
                    ReplyTo = ea.BasicProperties.ReplyTo,
                    Body = jsonBody,
                    Waiter = new(TaskCreationOptions.RunContinuationsAsynchronously)
                });
                await this.channel.BasicAckAsync(ea.DeliveryTag, false);
            }
            catch (Exception ex)
            {
                this.logger.LogTagError("RabbitConsumer", ex, "转发消息失败");
                await this.SafeNack(this.channel, ea.DeliveryTag);
            }
            finally
            {
                await this.ExitAsync();
            }
        };
        this.consumerTag = await this.channel.BasicConsumeAsync(this.QueueName, false, this.consumer);
    }
    private Func<string, Task<string>> CreateConsumerHandler(IServiceProvider serviceProvider, MethodInfo methodInfo)
    {
        var targetType = methodInfo.DeclaringType;
        var target = serviceProvider.GetService(targetType);
        var methodExecutor = ObjectMethodExecutor.Create(methodInfo, targetType.GetTypeInfo());
        var messageType = methodInfo.GetParameters().FirstOrDefault().ParameterType;
        var returnType = methodInfo.ReturnType;
        var isVoid = returnType == typeof(void) || returnType == typeof(Task) || returnType == typeof(ValueTask);
        Func<string, Task<string>> consumerHandler = null;
        Func<string, object> parametersGetter = messageType == typeof(string) ? f => f : f => TheaJsonSerializer.Deserialize(f, messageType);
        if (isVoid)
        {
            consumerHandler = methodExecutor.IsMethodAsync ? async message =>
            {
                var parameters = parametersGetter.Invoke(message);
                await methodExecutor.ExecuteAsync(target, [parameters]);
                return null;
            }
            : message =>
            {
                var parameters = parametersGetter.Invoke(message);
                methodExecutor.Execute(target, [parameters]);
                return Task.FromResult<string>(null);
            };
        }
        else
        {
            Func<object, string> resultGetter = returnType == typeof(string) ? f => f.ToString() : f => f.ToJson();
            if (methodExecutor.IsMethodAsync)
            {
                consumerHandler = async message =>
                {
                    var parameters = parametersGetter.Invoke(message);
                    var result = await methodExecutor.ExecuteAsync(target, [parameters]);
                    return resultGetter.Invoke(result);
                };
            }
            else
            {
                consumerHandler = message =>
                {
                    var parameters = parametersGetter.Invoke(message);
                    var result = methodExecutor.Execute(target, [parameters]);
                    return Task.FromResult(resultGetter.Invoke(result));
                };
            }
        }
        return consumerHandler;
    }
    private async Task<bool> TryEnterAsync()
    {
        await this.lifecycleLocker.WaitAsync();
        try
        {
            if (Volatile.Read(ref this.state) != 1)
                return false;
            Interlocked.Increment(ref this.workers);
            return true;
        }
        finally
        {
            this.lifecycleLocker.Release();
        }
    }
    private async Task ExitAsync()
    {
        await this.lifecycleLocker.WaitAsync();
        try
        {
            var remainingWorkers = Interlocked.Decrement(ref this.workers);
            if (remainingWorkers > 0) return;
            if (remainingWorkers < 0)
                throw new InvalidOperationException($"RabbitConsumer workers小于0，QueueName: {this.QueueName}, ConsumerId: {this.ConsumerId}");
            if (Volatile.Read(ref this.state) is 0 or 2)
                this.workersWaiter?.TrySetResult(true);
        }
        finally
        {
            this.lifecycleLocker.Release();
        }
    }
}