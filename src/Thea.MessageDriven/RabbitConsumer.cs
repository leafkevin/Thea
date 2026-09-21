using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
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

class RabbitConsumer : IDisposable
{
    private readonly MessageDrivenService parent;
    private readonly ILogger<RabbitConsumer> logger;
    private readonly QueueType queueType;
    private readonly Dictionary<string, (Type, Type, Func<object, Task<object>>)> exchangeHandlers;

    private ConnectionFactory factory;
    private CancellationTokenSource cancellationSource = null;
    private IConnection connection = null;
    private IChannel channel = null;
    private AsyncEventingBasicConsumer consumer = null;
    private string consumerTag;
    private readonly SemaphoreSlim lifecycleLock = new(1, 1);
    private readonly SemaphoreWaiter semaphoreWaiter = new();
    private readonly TimeSpan shutdownTimeout;
    public volatile int PrefetchCount;
    public volatile bool IsLogEnabled;
    public string ConsumerId { get; private set; }
    public string QueueName { get; private set; }
    public bool IsActivated => (this.connection?.IsOpen ?? false) && (this.channel?.IsOpen ?? false) && (this.consumer?.IsRunning ?? false);
    public bool IsBusying => this.semaphoreWaiter.IsBusying;

    public RabbitConsumer(string queueName, string consumerId, MessageDrivenService parent, IServiceProvider serviceProvider, QueueType queueType, int prefetchCount = 250, Dictionary<string, MethodInfo> exchangeMethodInfos = null)
    {
        this.parent = parent;
        this.QueueName = queueName;
        this.ConsumerId = consumerId;
        this.PrefetchCount = prefetchCount;
        this.queueType = queueType;
        this.logger = serviceProvider.GetService<ILogger<RabbitConsumer>>();
        var configuration = serviceProvider.GetService<IConfiguration>();
        var user = configuration.GetValue<string>("MessageDriven:User");
        var password = configuration.GetValue<string>("MessageDriven:Password");
        this.shutdownTimeout = parent.heartbeatCycle * 2;
        if (exchangeMethodInfos != null)
        {
            exchangeHandlers = new();
            foreach (var exchange in exchangeMethodInfos.Keys)
            {
                var consumerInvoker = exchangeMethodInfos[exchange];
                var targetType = consumerInvoker.DeclaringType;
                var target = serviceProvider.GetService(targetType);
                var methodExecutor = ObjectMethodExecutor.Create(consumerInvoker, targetType.GetTypeInfo());
                var messageType = consumerInvoker.GetParameters().FirstOrDefault().ParameterType;
                var returnType = consumerInvoker.ReturnType;
                var isVoid = returnType == typeof(void) || returnType == typeof(Task) || returnType == typeof(ValueTask);
                Func<object, Task<object>> consumerHandler = null;
                if (isVoid)
                {
                    returnType = typeof(object);
                    consumerHandler = methodExecutor.IsMethodAsync ? async message =>
                    {
                        await methodExecutor.ExecuteAsync(target, [message]);
                        return null;
                    }
                    : message =>
                    {
                        methodExecutor.Execute(target, [message]);
                        return Task.FromResult<object>(null);
                    };
                }
                else
                {
                    consumerHandler = methodExecutor.IsMethodAsync ? async message =>
                        await methodExecutor.ExecuteAsync(target, [message]) : message => Task.FromResult(methodExecutor.Execute(target, [message]));
                }
                this.exchangeHandlers.TryAdd(exchange, (messageType, returnType, consumerHandler));
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
                { "connection_name", this.ConsumerId},
                { "client_api", "Thea.MessageDriven" }
            }
        };
    }
    public async Task StartAsync()
    {
        await this.lifecycleLock.WaitAsync();
        try
        {
            await this.StartCoreAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine();
            this.logger.LogTagError("RabbitConsumer", ex, $"StartAsync，启动RabbitMQ消费者失败, ConsumerId: {this.ConsumerId}, QueueName: {this.QueueName}");
        }
        finally
        {
            this.lifecycleLock.Release();
        }
    }
    public async Task ShutdownAsync()
    {
        await this.lifecycleLock.WaitAsync();
        try
        {
            await this.ShutdownCoreAsync();
        }
        catch (Exception ex)
        {
            this.logger.LogTagError("RabbitConsumer", ex, $"ShutdownAsync，关闭RabbitMQ消费者失败, ConsumerId: {this.ConsumerId}, QueueName: {this.QueueName}");
        }
        finally
        {
            this.lifecycleLock.Release();
        }
    }
    public async Task RestartAsync()
    {
        await this.lifecycleLock.WaitAsync();
        try
        {
            await this.ShutdownCoreAsync();
            await this.StartCoreAsync();
        }
        finally
        {
            this.lifecycleLock.Release();
        }
    }
    public async Task<uint> MessageCount()
    {
        var currentChannel = this.channel;
        if (!(currentChannel?.IsOpen ?? false)) return 0;
        try
        {
            return await currentChannel.MessageCountAsync(this.QueueName);
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
    public void Dispose() => this.ShutdownAsync().GetAwaiter().GetResult();
    private async Task StartCoreAsync()
    {
        if (this.IsActivated) return;
        if (this.cancellationSource != null)
            await this.ShutdownCoreAsync();
        else await this.DisposeAsync();
        try
        {
            this.cancellationSource = new();
            this.connection = await this.factory.CreateConnectionAsync(this.parent.tcpEndPoints, this.ConsumerId);
            this.channel = await this.connection.CreateChannelAsync();
            await this.channel.BasicQosAsync(0, (ushort)this.PrefetchCount, false);
            switch (this.queueType)
            {
                case QueueType.Heartbeat:
                    await this.channel.QueueDeclareAsync(this.QueueName, false, true, false);
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
            await this.DisposeAsync();
            throw;
        }
    }
    private async Task ShutdownCoreAsync()
    {
        var currentCancellationSource = this.cancellationSource;
        if (currentCancellationSource == null)
        {
            await this.DisposeAsync();
            return;
        }
        currentCancellationSource.Cancel();
        var currentChannel = this.channel;
        var currentConsumerTag = this.consumerTag;
        if (!string.IsNullOrEmpty(currentConsumerTag) && (currentChannel?.IsOpen ?? false))
        {
            try
            {
                using var cancelTimeoutSource = new CancellationTokenSource(this.shutdownTimeout);
                await currentChannel.BasicCancelAsync(currentConsumerTag, false, cancelTimeoutSource.Token);
            }
            catch (Exception ex)
            {
                this.logger.LogTagError("RabbitConsumer", ex, $"ShutdownCoreAsync，取消消费者失败, ConsumerId: {this.ConsumerId}, QueueName: {this.QueueName}");
            }
        }
        if (await this.semaphoreWaiter.WaitAsync(this.shutdownTimeout))
            this.logger.LogTagError("RabbitConsumer", $"ShutdownCoreAsync，等待消费者处理完成超时, ConsumerId: {this.ConsumerId}, QueueName: {this.QueueName}, Timeout: {this.shutdownTimeout.TotalSeconds}s");
        await this.DisposeAsync();
    }
    private async Task DisposeAsync()
    {
        var channelToDispose = this.channel;
        var connectionToDispose = this.connection;
        var cancellationSourceToDispose = this.cancellationSource;
        this.channel = null;
        this.connection = null;
        this.cancellationSource = null;
        this.consumer = null;
        this.consumerTag = null;
        if (channelToDispose != null)
        {
            try
            {
                await channelToDispose.DisposeAsync();
            }
            catch (Exception ex)
            {
                this.logger.LogTagError("RabbitConsumer", ex, $"DisposeAsync，关闭Channel失败, ConsumerId: {this.ConsumerId}, QueueName: {this.QueueName}");
            }
        }
        if (connectionToDispose != null)
        {
            try
            {
                await connectionToDispose.DisposeAsync();
            }
            catch (Exception ex)
            {
                this.logger.LogTagError("RabbitConsumer", ex, $"DisposeAsync，关闭Connection失败, ConsumerId: {this.ConsumerId}, QueueName: {this.QueueName}");
            }
        }
        cancellationSourceToDispose?.Dispose();
    }
    private async Task SafeNack(IChannel consumerChannel, ulong deliveryTag)
    {
        try
        {
            if (consumerChannel?.IsOpen ?? false)
                await consumerChannel.BasicNackAsync(deliveryTag, false, true);
        }
        catch (Exception ex)
        {
            this.logger.LogTagError("RabbitConsumer", ex, $"SafeNack，拒绝消息失败, ConsumerId: {this.ConsumerId}, QueueName: {this.QueueName}, DeliveryTag: {deliveryTag}");
        }
    }
    private async Task BindUserMessageHandler()
    {
        var consumerChannel = this.channel;
        var consumerCancellationSource = this.cancellationSource;
        this.consumer = new AsyncEventingBasicConsumer(consumerChannel);
        //用户消息队列
        this.consumer.ReceivedAsync += async (model, ea) =>
        {
            this.semaphoreWaiter.Enter();
            IDisposable scopeObj = null;

            string traceId = null;
            string jsonBody = null;
            try
            {
                //消费者关闭过程中收到的在途投递不再处理，Channel关闭后由RabbitMQ重新入队
                if (consumerCancellationSource.IsCancellationRequested)
                    return;
                var iLoop = 0;
                Exception exception = null;
                bool isSuccess = true;
                jsonBody = Encoding.UTF8.GetString(ea.Body.Span);

                //内部消息，交给消息总分发处处理
                string result = null;
                var createdAt = DateTime.UtcNow;
                var messageId = ea.BasicProperties.MessageId;
                var messageType = ea.BasicProperties.Type;
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
                                    (var parameterType, var returnType, var typedHandler) = this.exchangeHandlers[ea.Exchange];
                                    var parameters = TheaJsonSerializer.Deserialize(jsonBody, parameterType);
                                    if (messageType == Consts.RpcMessage)
                                    {
                                        //Console.WriteLine($"RpcMessage, MessageId: {message.MessageId}, From:{message.From}, DateTime: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                                        //处理RPC消息完毕，发送RPC结果给RPC结果队列，并设置来时请求结果
                                        var rpcResult = await typedHandler.Invoke(parameters);
                                        result = rpcResult.ToJson();
                                    }
                                    else await typedHandler.Invoke(parameters);
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
                                        UpdatedAt = DateTime.Now
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
                                //Console.WriteLine($"RpcMessage, Publish Response, MessageId: {message.MessageId}, From:{message.From}, DateTime: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                                var replyToQueue = ea.BasicProperties.ReplyTo;
                                var rpcMessageType = isSuccess ? Consts.RpcResponse : Consts.RpcFailure;
                                await this.parent.rabbitProducer.PublishAsync(Consts.DefaultExchange, replyToQueue, new BasicProperties
                                {
                                    Persistent = true,
                                    Type = rpcMessageType,
                                    AppId = ea.BasicProperties.AppId,
                                    DeliveryMode = DeliveryModes.Persistent,
                                    MessageId = messageId,
                                    CorrelationId = messageId,
                                    Headers = new Dictionary<string, object> { { "TraceId", traceId } }
                                }, result);
                            }
                            //RPC消息直接跳过，因为异常已经返回到前端了
                            if (!isSuccess && messageType == Consts.UserMessage)
                                throw exception;
                        }
                        break;
                }
                await consumerChannel.BasicAckAsync(ea.DeliveryTag, false);
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
                    IsJsonMessage = true,
                    Body = jsonBody
                }.ToJson();
                this.logger.LogTagError("RabbitConsumer", ex, $"BindUserMessageHandler，处理用户消息异常, ConsumerId: {this.ConsumerId}, Message: {messageJson}");
                Console.WriteLine($"处理用户消息异常, Message: {messageJson}, Exception: {ex}");
                await this.SafeNack(consumerChannel, ea.DeliveryTag);
            }
            finally
            {
                scopeObj?.Dispose();
                this.semaphoreWaiter.Exit();
            }
        };
        this.consumerTag = await consumerChannel.BasicConsumeAsync(this.QueueName, false, this.consumer);
    }
    private async Task BindHeartbeatHandler()
    {
        var consumerChannel = this.channel;
        var consumerCancellationSource = this.cancellationSource;
        this.consumer = new AsyncEventingBasicConsumer(consumerChannel);
        this.consumer.ReceivedAsync += async (model, ea) =>
        {
            this.semaphoreWaiter.Enter();
            try
            {
                if (consumerCancellationSource.IsCancellationRequested)
                    return;
                var serviceId = Encoding.UTF8.GetString(ea.Body.Span);
                if (ea.BasicProperties.AppId == this.parent.AppId)
                {
                    await this.parent.ProcessMessage(new Message
                    {
                        MessageId = ea.BasicProperties.MessageId,
                        Type = ea.BasicProperties.Type,
                        Body = serviceId
                    });
                }
                await consumerChannel.BasicAckAsync(ea.DeliveryTag, false);
            }
            catch (Exception ex)
            {
                this.logger.LogTagError("RabbitConsumer", ex, $"BindHeartbeatHandler，处理心跳消息失败, ConsumerId: {this.ConsumerId}");
                await this.SafeNack(consumerChannel, ea.DeliveryTag);
            }
            finally
            {
                this.semaphoreWaiter.Exit();
            }
        };
        this.consumerTag = await consumerChannel.BasicConsumeAsync(this.QueueName, false, this.consumer);
    }
    private async Task BindRpcResultHandler()
    {
        var consumerChannel = this.channel;
        var consumerCancellationSource = this.cancellationSource;
        this.consumer = new AsyncEventingBasicConsumer(consumerChannel);
        this.consumer.ReceivedAsync += async (model, ea) =>
        {
            this.semaphoreWaiter.Enter();
            try
            {
                if (consumerCancellationSource.IsCancellationRequested)
                    return;

                var result = Encoding.UTF8.GetString(ea.Body.Span);
                //Console.WriteLine($"RpcResponse, MessageId: {message.MessageId}, From:{message.From}, RoutingKey:{ea.RoutingKey}, DateTime: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                await this.parent.ProcessMessage(new Message
                {
                    MessageId = ea.BasicProperties.MessageId,
                    Type = ea.BasicProperties.Type,
                    IsJsonMessage = ea.BasicProperties.Type == Consts.RpcResponse,
                    Body = result
                });
                await consumerChannel.BasicAckAsync(ea.DeliveryTag, false);
            }
            catch (Exception ex)
            {
                this.logger.LogTagError("RabbitConsumer", ex, $"BindRpcResultHandler，处理RPC结果失败, ConsumerId: {this.ConsumerId}");
                await this.SafeNack(consumerChannel, ea.DeliveryTag);
            }
            finally
            {
                this.semaphoreWaiter.Exit();
            }
        };
        this.consumerTag = await consumerChannel.BasicConsumeAsync(this.QueueName, false, this.consumer);
    }
    private async Task BindTransferHandler()
    {
        var consumerChannel = this.channel;
        var consumerCancellationSource = this.cancellationSource;
        this.consumer = new AsyncEventingBasicConsumer(consumerChannel);
        this.consumer.ReceivedAsync += async (model, ea) =>
        {
            this.semaphoreWaiter.Enter();
            string exMessage = null;
            try
            {
                if (consumerCancellationSource.IsCancellationRequested)
                    return;

                var jsonBody = Encoding.UTF8.GetString(ea.Body.Span);
                var headers = ea.BasicProperties.Headers;
                string exchange = null, routingKey = null, traceId = null;
                if (headers.TryGetValue("Exchange", out var exchangeBytes))
                    exchange = Encoding.UTF8.GetString((byte[])exchangeBytes);
                if (headers.TryGetValue("RoutingKey", out var routingKeyBytes))
                    routingKey = Encoding.UTF8.GetString((byte[])routingKeyBytes);
                if (headers.TryGetValue("TraceId", out var traceIdBytes))
                    traceId = Encoding.UTF8.GetString((byte[])traceIdBytes);

                var message = new Message
                {
                    MessageId = ea.BasicProperties.MessageId,
                    Type = ea.BasicProperties.Type,
                    Exchange = exchange,
                    RoutingKey = routingKey,
                    TraceId = traceId,
                    ReplyTo = ea.BasicProperties.ReplyTo,
                    IsJsonMessage = true,
                    Body = jsonBody
                };

                var timeoutSeconds = this.parent.heartbeatCycle.TotalSeconds;
                exMessage = $"转发消息超时, 耗时{timeoutSeconds}s, Now: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}, Message: {message.ToJson()}";
                message.Waiter = new TaskCompletionSource<bool>();
                await this.parent.ProcessMessage(message);
                await message.Waiter.WaitAsync(this.parent.heartbeatCycle, exMessage);
                await consumerChannel.BasicAckAsync(ea.DeliveryTag, false);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"{exMessage}, Exception: {ex}");
                this.logger.LogTagError("RabbitConsumer", ex, $"BindTransferHandler，{exMessage}");
                await this.SafeNack(consumerChannel, ea.DeliveryTag);
            }
            finally
            {
                this.semaphoreWaiter.Exit();
            }
        };
        this.consumerTag = await consumerChannel.BasicConsumeAsync(this.QueueName, false, this.consumer);
    }
}