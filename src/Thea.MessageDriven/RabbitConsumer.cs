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
    private readonly Action<ExecLog> addLogsHandler;
    private readonly ILogger<RabbitConsumer> logger;
    private readonly QueueType queueType;
    private readonly int prefetchCount;
    private readonly Dictionary<string, (Type, Type, Func<object, Task<object>>)> exchangeHandlers;

    private ConnectionFactory factory;
    private CancellationTokenSource cancellationSource = null;
    private IConnection connection = null;
    private IChannel channel = null;
    private AsyncEventingBasicConsumer consumer = null;

    private volatile bool isDeferClose = false;
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
        this.prefetchCount = prefetchCount;
        this.queueType = queueType;
        this.addLogsHandler = parent.AddLogs;
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
        await this.channel.BasicQosAsync(0, (ushort)this.prefetchCount, false);
        switch (this.queueType)
        {
            case QueueType.Message: await this.BindUserMessageHandler(); break;
            case QueueType.Transfer: await this.BindTransferHandler(); break;
        }
    }
    public async Task Start(string exclusiveExchange, string exclusiveBindingKey)
    {
        if (this.IsActivated) return;
        this.cancellationSource = new();
        this.connection = await this.factory.CreateConnectionAsync(this.parent.tcpEndPoints, this.ConsumerId);
        this.channel = await this.connection.CreateChannelAsync();

        if (this.queueType == QueueType.Heartbeat || this.queueType == QueueType.RpcResult)
        {
            await this.channel.QueueDeclareAsync(this.QueueName, false, true, false);
            await this.channel.QueueBindAsync(this.QueueName, exclusiveExchange, exclusiveBindingKey);
        }
        await this.channel.BasicQosAsync(0, (ushort)this.prefetchCount, false);
        switch (this.queueType)
        {
            case QueueType.Heartbeat: await this.BindHeartbeatHandler(); break;
            case QueueType.RpcResult: await this.BindRpcResultHandler(); break;
        }
    }
    public async Task Shutdown(bool isForce = false)
    {
        if (this.cancellationSource == null) return;
        this.cancellationSource.Cancel();
        this.isDeferClose = !isForce && !this.IsRunning;
        if (isForce || !this.IsRunning)
            await this.Close();
    }
    public async Task<uint> MessageCount() => await this.channel.MessageCountAsync(this.QueueName);
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

            var message = jsonBody.JsonTo<Message<string>>();
            //兼容现非框架队列消息
            if (message.MessageId == null)
            {
                message.MessageId = ObjectId.NewId();
                message.Type = MessageType.Message;
                message.Body = jsonBody;
            }
            //内部消息，交给消息总分发处处理
            string result = null;
            var createdAt = DateTime.Now;
            switch (message.Type)
            {
                case MessageType.Message:
                case MessageType.RpcMessage:
                    {
                        //赋值TraceId，用于日志跟踪
                        if (!string.IsNullOrEmpty(message.TraceId))
                            this.logger.BeginScope(new LogEntity { TraceId = message.TraceId });

                        while (iLoop < 3)
                        {
                            try
                            {
                                (var parameterType, var returnType, var typedHandler) = this.exchangeHandlers[ea.Exchange];
                                var parameters = TheaJsonSerializer.Deserialize(message.Body, parameterType);
                                if (message.Type == MessageType.RpcMessage)
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
                        if (this.IsLogEnabled || !isSuccess)
                        {
                            var logId = ObjectId.NewId();
                            this.addLogsHandler.Invoke(new ExecLog
                            {
                                LogId = logId,
                                ExchangeId = ea.Exchange,
                                RoutingKey = ea.RoutingKey,
                                Queue = this.QueueName,
                                Body = jsonBody,
                                IsSuccess = isSuccess,
                                Result = result,
                                RetryTimes = iLoop,
                                UpdatedAt = DateTime.Now
                            });
                            var resultBody = isSuccess ? "success" : "failed";
                            this.logger.LogEntity(new LogEntity
                            {
                                Id = logId,
                                ApiType = (int)ApiType.LocalInvoke,
                                Tag = "RabbitConsumer",
                                Body = $"consumed {resultBody}, queue: {this.QueueName}, exchange: {ea.Exchange}, routingKey: {ea.RoutingKey}",
                                LogLevel = (int)(isSuccess ? LogLevel.Information : LogLevel.Error),
                                Exception = exception,
                                Request = jsonBody,
                                Response = result,
                                Elapsed = (int)DateTime.Now.Subtract(createdAt).TotalMilliseconds
                            });
                        }
                        if (message.Type == MessageType.RpcMessage)
                        {
                            var messageType = isSuccess ? MessageType.RpcResponse : MessageType.RpcFailure;
                            var rpcMessage = new Message
                            {
                                MessageId = message.MessageId,
                                From = message.From,
                                Type = messageType,
                                Exchange = message.Exchange,
                                RoutingKey = message.RoutingKey,
                                TraceId = message.TraceId,
                                Body = result
                            };
                            //Console.WriteLine($"RpcMessage, Publish Response, MessageId: {message.MessageId}, From:{message.From}, DateTime: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                            await this.parent.rabbitProducer.Publish(Consts.RpcExchange, message.From, rpcMessage.ToJson());
                        }
                        //RPC消息直接跳过，因为异常已经返回到前端了
                        if (!isSuccess && message.Type == MessageType.Message)
                            throw exception;
                    }
                    break;
                case MessageType.WaitStarting:
                case MessageType.WaitShutdowning:
                    Console.WriteLine($"队列 {this.QueueName} 收到 {message.Type} 标志消息!!");
                    //通知到所有节点，当前队列消息已消费完毕，累加消息完成的队列个数
                    await this.parent.rabbitProducer.Publish(Consts.HeartbeatExchange, Consts.FanoutRoutingKey, message.ToJson());
                    break;
                default: throw new Exception("Unknown message type");
            }
            await channel.BasicAckAsync(ea.DeliveryTag, false);

            //再延迟停止
            if (this.isDeferClose)
                await this.Close();
        };
        await channel.BasicConsumeAsync(this.QueueName, false, this.consumer);
    }
    private async Task BindHeartbeatHandler()
    {
        this.consumer = new AsyncEventingBasicConsumer(channel);
        this.consumer.ReceivedAsync += async (model, ea) =>
        {
            //先暂停消费
            if (this.cancellationSource.IsCancellationRequested)
                return;
            var jsonBody = Encoding.UTF8.GetString(ea.Body.Span);
            var message = jsonBody.JsonTo<Message<string>>();
            switch (message.Type)
            {
                case MessageType.Heartbeat:
                    if (message.From == this.parent.AppId)
                    {
                        this.parent.ProcessMessage(new Message
                        {
                            MessageId = message.MessageId,
                            Type = message.Type,
                            Body = message.Body
                        });
                    }
                    break;
                case MessageType.WaitStarting:
                case MessageType.WaitShutdowning:
                    if (message.From == this.parent.AppId)
                    {
                        var syncMessage = new Message
                        {
                            MessageId = message.MessageId,
                            Type = message.Type,
                            Body = message.Body,
                            Waiter = new()
                        };
                        int retryTimes = 0;
                        while (retryTimes < 3)
                        {
                            try
                            {
                                this.parent.ProcessMessage(syncMessage);
                                await syncMessage.Waiter.WithTimeout(TimeSpan.FromSeconds(15));
                                retryTimes++;
                                break;
                            }
                            catch (TimeoutException ex)
                            {
                                this.logger.LogTagError("BindHeartbeatHandler", ex, $"{message.Type} message timeout 15s, MessageId: {message.MessageId}, Now: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                                Console.WriteLine($"{message.Type} message timeout 15s, MessageId: {message.MessageId}, Now: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                                continue;
                            }
                            catch (Exception ex)
                            {
                                this.logger.LogTagError("BindHeartbeatHandler", ex, $"{message.Type} message exception, MessageId: {message.MessageId}, Now: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                                Console.WriteLine($"Transfer message exception, MessageId: {message.MessageId}, Now: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                                continue;
                            }
                        }
                        syncMessage.Waiter = null;
                    }
                    break;
                default: throw new Exception("Unknown message type");
            }
            await channel.BasicAckAsync(ea.DeliveryTag, false);
            //再延迟停止
            if (this.isDeferClose)
                await this.Close();
        };
        await channel.BasicConsumeAsync(this.QueueName, false, this.consumer);
    }
    private async Task BindRpcResultHandler()
    {
        this.consumer = new AsyncEventingBasicConsumer(channel);
        this.consumer.ReceivedAsync += async (model, ea) =>
        {
            //先暂停消费
            if (this.cancellationSource.IsCancellationRequested)
                return;

            var jsonBody = Encoding.UTF8.GetString(ea.Body.Span);
            var message = jsonBody.JsonTo<Message<string>>();
            //Console.WriteLine($"RpcResponse, MessageId: {message.MessageId}, From:{message.From}, RoutingKey:{ea.RoutingKey}, DateTime: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            this.parent.SetRpcResult(message.MessageId, message);
            await channel.BasicAckAsync(ea.DeliveryTag, false);
            //再延迟停止
            if (this.isDeferClose)
                await this.Close();
        };
        await channel.BasicConsumeAsync(this.QueueName, false, this.consumer);
    }
    private async Task BindTransferHandler()
    {
        this.consumer = new AsyncEventingBasicConsumer(channel);
        this.consumer.ReceivedAsync += async (model, ea) =>
        {
            int retryTimes = 0;
            while (retryTimes < 3)
            {
                if (this.cancellationSource.IsCancellationRequested)
                    return;
                var jsonBody = Encoding.UTF8.GetString(ea.Body.Span);
                var message = jsonBody.JsonTo<Message>();
                try
                {
                    message.Waiter = new();
                    this.parent.ProcessMessage(message);
                    await message.Waiter.WithTimeout(TimeSpan.FromSeconds(15));
                    retryTimes++;
                    break;
                }
                catch (TimeoutException ex)
                {
                    this.logger.LogTagError("BindTransferHandler", ex, $"Transfer message timeout 15s, MessageId: {message.MessageId}, Now: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                    Console.WriteLine($"Transfer message timeout 15s, MessageId: {message.MessageId}, Now: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                    continue;
                }
                catch (Exception ex)
                {
                    this.logger.LogTagError("BindTransferHandler", ex, $"Transfer message exception, MessageId: {message.MessageId}, Now: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                    Console.WriteLine($"Transfer message exception, MessageId: {message.MessageId}, Now: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                    continue;
                }
            }
            await channel.BasicAckAsync(ea.DeliveryTag, false);
            //再延迟停止
            if (this.isDeferClose)
                await this.Close();
        };
        await channel.BasicConsumeAsync(this.QueueName, false, this.consumer);
    }
}