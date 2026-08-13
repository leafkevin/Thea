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

class RabbitConsumer
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
    private volatile bool isDeferClose = false;
    public volatile int PrefetchCount;
    public volatile bool IsLogEnabled;
    public string ConsumerId { get; private set; }
    public string QueueName { get; private set; }
    public bool IsActivated => (this.connection?.IsOpen ?? false) && (this.channel?.IsOpen ?? false);
    public bool IsRunning => this.consumer?.IsRunning ?? false;

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
            RequestedHeartbeat = TimeSpan.FromSeconds(10),
            NetworkRecoveryInterval = TimeSpan.FromSeconds(5),
            ClientProperties = new Dictionary<string, object>()
            {
                { "connection_name", this.ConsumerId},
                { "client_api", "Thea.MessageDriven" }
            }
        };
    }
    public async Task Start()
    {
        if (this.IsActivated) return;
        this.cancellationSource = new();
        this.connection = await this.factory.CreateConnectionAsync(this.parent.tcpEndPoints, this.ConsumerId);
        this.channel = await this.connection.CreateChannelAsync();
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
        await this.channel.BasicQosAsync(0, (ushort)this.PrefetchCount, false);
    }
    public async Task Shutdown(bool isForce = false)
    {
        if (this.cancellationSource == null) return;
        this.cancellationSource.Cancel();
        this.isDeferClose = !isForce && this.IsRunning;
        await this.channel.BasicCancelAsync(this.consumerTag);
        if (isForce || !this.IsRunning)
            await this.Close();
    }
    public async Task<uint> MessageCount() => this.channel != null
        ? await this.channel.MessageCountAsync(this.QueueName) : 0;
    private async Task Close()
    {
        if (this.channel != null)
        {
            await channel.DisposeAsync();
            this.channel = null;
        }
        if (this.connection != null)
        {
            await this.connection.DisposeAsync();
            this.connection = null;
        }
        this.cancellationSource.Dispose();
        this.cancellationSource = null;
    }
    private async Task BindUserMessageHandler()
    {
        this.consumer = new AsyncEventingBasicConsumer(channel);
        //用户消息队列
        this.consumer.ReceivedAsync += async (model, ea) =>
        {
            //先暂停消费
            if (this.cancellationSource.IsCancellationRequested)
                return;

            var iLoop = 0;
            Exception exception = null;
            bool isSuccess = true;
            var jsonBody = Encoding.UTF8.GetString(ea.Body.Span);

            //内部消息，交给消息总分发处处理
            string result = null;
            var createdAt = DateTime.Now;
            var messageId = ea.BasicProperties.MessageId;
            var messageType = ea.BasicProperties.Type;
            var headers = ea.BasicProperties.Headers;
            string traceId = null;
            if (headers != null && headers.TryGetValue("TraceId", out var objValue))
                traceId = Encoding.UTF8.GetString((byte[])headers["TraceId"]);
            switch (messageType)
            {
                case Consts.UserMessage:
                case Consts.RpcMessage:
                    {
                        //赋值TraceId，用于日志跟踪
                        IDisposable scopeObj = null;
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
                        var hasScopeState = ScopeState.TryGetState(out var lastScopeState);
                        if (!hasScopeState || hasScopeState && lastScopeState.IsEnabled)
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
                                Elapsed = (int)DateTime.Now.Subtract(createdAt).TotalMilliseconds
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
                        scopeObj?.Dispose();
                        //RPC消息直接跳过，因为异常已经返回到前端了
                        if (!isSuccess && messageType == Consts.UserMessage)
                            throw exception;
                    }
                    break;
                default: throw new Exception("Unknown message type");
            }
            await channel.BasicAckAsync(ea.DeliveryTag, false);

            //再延迟停止
            if (this.isDeferClose)
                await this.Close();
        };
        this.consumerTag = await channel.BasicConsumeAsync(this.QueueName, false, this.consumer);
    }
    private async Task BindHeartbeatHandler()
    {
        this.consumer = new AsyncEventingBasicConsumer(channel);
        this.consumer.ReceivedAsync += async (model, ea) =>
        {
            //先暂停消费
            if (this.cancellationSource.IsCancellationRequested)
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
            await channel.BasicAckAsync(ea.DeliveryTag, false);
            //再延迟停止
            if (this.isDeferClose)
                await this.Close();
        };
        this.consumerTag = await channel.BasicConsumeAsync(this.QueueName, false, this.consumer);
    }
    private async Task BindRpcResultHandler()
    {
        this.consumer = new AsyncEventingBasicConsumer(channel);
        this.consumer.ReceivedAsync += async (model, ea) =>
        {
            //先暂停消费
            if (this.cancellationSource.IsCancellationRequested)
                return;

            var result = Encoding.UTF8.GetString(ea.Body.Span);
            //Console.WriteLine($"RpcResponse, MessageId: {message.MessageId}, From:{message.From}, RoutingKey:{ea.RoutingKey}, DateTime: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            this.parent.SetRpcResult(ea.BasicProperties.MessageId, new RpcResponse
            {
                Type = ea.BasicProperties.Type,
                Body = result
            });
            await channel.BasicAckAsync(ea.DeliveryTag, false);
            //再延迟停止
            if (this.isDeferClose)
                await this.Close();
        };
        this.consumerTag = await channel.BasicConsumeAsync(this.QueueName, false, this.consumer);
    }
    private async Task BindTransferHandler()
    {
        this.consumer = new AsyncEventingBasicConsumer(channel);
        this.consumer.ReceivedAsync += async (model, ea) =>
        {
            if (this.cancellationSource.IsCancellationRequested)
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

            var timeoutSeconds = this.parent.HeartbeatCycle.TotalSeconds;
            var exMessage = $"转发消息超时, 耗时{timeoutSeconds}s, Now: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}, Message: {message.ToJson()}, ";

            try
            {
                message.Waiter = new TaskCompletionSource<bool>();
                await this.parent.ProcessMessage(message);
                await message.Waiter.WaitAsync(this.parent.HeartbeatCycle, exMessage, this.cancellationSource.Token);
            }
            catch (Exception ex)
            {
                this.logger.LogTagError("BindTransferHandler", ex, exMessage);
                Console.WriteLine(exMessage);
            }
            await channel.BasicAckAsync(ea.DeliveryTag, false);
            //再延迟停止
            if (this.isDeferClose)
                await this.Close();
        };
        this.consumerTag = await channel.BasicConsumeAsync(this.QueueName, false, this.consumer);
    }
}