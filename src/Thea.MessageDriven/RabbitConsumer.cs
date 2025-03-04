using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Protocol;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Thea.Json;
using Thea.Logging;

namespace Thea.MessageDriven;

class RabbitConsumer
{
    private readonly CancellationTokenSource cancellationSource = new CancellationTokenSource();
    private readonly MessageDrivenService parent;
    private readonly ConnectionFactory factory;
    private readonly Action<ExecLog> addLogsHandler;
    private readonly ILogger<RabbitConsumer> logger;
    private readonly string connectionId;
    private readonly bool isExclusive = false;
    private readonly Dictionary<string, (Type, Type, Func<object, Task<object>>)> exchangeHandlers;

    private IConnection connection = null;
    private volatile IChannel channel = null;
    private volatile bool isRunning = false;
    private volatile bool isStarted = false;
    private volatile bool isDeferClose = false;

    public volatile bool IsLogEnabled;
    public string ConsumerId { get; private set; }
    public string QueueName { get; private set; }
    public bool IsActivated => this.channel != null && this.channel.IsOpen;

    public RabbitConsumer(string queueName, MessageDrivenService parent, IServiceProvider serviceProvider, bool isExclusive, Dictionary<string, MethodInfo> exchangeMethodInfos = null)
    {
        this.parent = parent;
        this.ConsumerId = ObjectId.NewId();
        this.QueueName = queueName;
        this.connectionId = queueName;
        if (!isExclusive) this.connectionId += $".{parent.NodeId}";
        this.addLogsHandler = parent.AddLogs;
        this.logger = serviceProvider.GetService<ILogger<RabbitConsumer>>();
        var configuration = serviceProvider.GetService<IConfiguration>();
        var url = configuration.GetValue<string>("MessageDriven:Url");
        var user = configuration.GetValue<string>("MessageDriven:User");
        var password = configuration.GetValue<string>("MessageDriven:Password");
        this.isExclusive = isExclusive;
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
            Uri = new Uri(url),
            UserName = user,
            Password = password,
            AutomaticRecoveryEnabled = true,
            RequestedHeartbeat = TimeSpan.FromSeconds(10),
            NetworkRecoveryInterval = TimeSpan.FromSeconds(5),
            ClientProperties = new Dictionary<string, object>()
            {
                { "connection_name", this.connectionId},
                { "client_api", "Thea.MessageDriven" }
            }
        };
    }
    public async Task Start()
    {
        if (this.isRunning || this.isStarted) return;
        this.connection = await this.factory.CreateConnectionAsync(this.connectionId);
        this.channel = await this.connection.CreateChannelAsync();
        ushort prefetchCount = 20;
        await this.channel.BasicQosAsync(0, prefetchCount, false);
        await this.BindHandler();
        this.isStarted = true;
    }
    public async Task Start(string exclusiveExchange, string exclusiveBindingKey)
    {
        if (this.isRunning || this.isStarted) return;
        this.connection = await this.factory.CreateConnectionAsync(this.connectionId);
        this.channel = await this.connection.CreateChannelAsync();

        if (this.isExclusive)
        {
            await this.channel.QueueDeclareAsync(this.QueueName, false, true, false);
            await this.channel.QueueBindAsync(this.QueueName, exclusiveExchange, exclusiveBindingKey);
        }

        ushort prefetchCount = 20;
        await this.channel.BasicQosAsync(0, prefetchCount, false);
        await this.BindHandler();
        this.isStarted = true;
    }
    public async Task RemoveQueue()
    {
        if (this.channel != null)
            await this.channel.QueueDeleteAsync(this.QueueName);
    }
    public async Task Shutdown()
    {
        this.cancellationSource.Cancel();
        if (this.isRunning) this.isDeferClose = true;
        else await this.Close();
    }
    private async Task Close()
    {
        if (this.channel != null)
        {
            await channel.CloseAsync();
            this.channel = null;
        }
        if (this.connection != null)
        {
            await this.connection.CloseAsync();
            this.connection = null;
        }
        this.cancellationSource.Dispose();
    }
    private async Task BindHandler()
    {
        var consumer = new AsyncEventingBasicConsumer(channel);
        if (this.isExclusive)
        {
            //心跳队列和rpc结果队列
            consumer.ReceivedAsync += async (model, ea) =>
            {
                //先暂停消费
                if (this.cancellationSource.IsCancellationRequested)
                    return;

                var jsonBody = Encoding.UTF8.GetString(ea.Body.Span);
                var message = jsonBody.JsonTo<Message<string>>();
                //内部消息，交给消息总分发处处理
                switch (message.Type)
                {
                    case MessageType.RpcResponse:
                    case MessageType.RpcFailure:
                        this.parent.SetRpcResult(message.MessageId, message);
                        break;

                    case MessageType.WaitForStart:
                    case MessageType.WaitForShutdown:
                    case MessageType.Heartbeat:
                        Console.WriteLine($"{message.Type} - heartbeat: {message.Body} received!");
                        if (message.AppId == this.parent.AppId)
                            this.parent.ProcessMessage(message);
                        break;
                    default: throw new Exception("Unknown message type");
                }
                await channel.BasicAckAsync(ea.DeliveryTag, false);

                //再延迟停止
                if (this.isDeferClose)
                    await this.Close();
            };
        }
        else
        {
            //用户消息队列
            consumer.ReceivedAsync += async (model, ea) =>
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
                switch (message.Type)
                {
                    case MessageType.Message:
                    case MessageType.RpcMessage:
                        {
                            while (iLoop < 3)
                            {
                                try
                                {
                                    (var parameterType, var returnType, var typedHandler) = this.exchangeHandlers[ea.Exchange];
                                    var parameters = TheaJsonSerializer.Deserialize(message.Body, parameterType);
                                    if (message.Type == MessageType.RpcMessage)
                                    {
                                        //处理RPC消息完毕，发送RPC结果给RPC结果队列，并设置来时请求结果
                                        var rpcResult = await typedHandler.Invoke(parameters);
                                        result = rpcResult.ToJson();
                                    }
                                    else await typedHandler.Invoke(parameters);
                                    break;
                                }
                                catch (Exception ex)
                                {
                                    isSuccess = false;
                                    exception = ex.InnerException ?? ex;
                                }
                                iLoop++;
                                Thread.Sleep(1000);
                            }
                            if (!isSuccess) result = exception.ToString();
                            var logInfo = new ExecLog
                            {
                                LogId = ObjectId.NewId(),
                                ExchangeId = ea.Exchange,
                                RoutingKey = ea.RoutingKey,
                                Queue = this.QueueName,
                                Body = jsonBody,
                                IsSuccess = isSuccess,
                                Result = result,
                                RetryTimes = iLoop,
                                UpdatedAt = DateTime.Now
                            };
                            if (this.IsLogEnabled || !isSuccess)
                            {
                                this.addLogsHandler.Invoke(logInfo);
                                if (!isSuccess) this.logger.LogTagError("RabbitConsumer", exception, $"Consume message failed, Message:{jsonBody}");
                            }
                            if (message.Type == MessageType.RpcMessage)
                            {
                                var messageType = isSuccess ? MessageType.RpcResponse : MessageType.RpcFailure;
                                var rpcMessage = new Message
                                {
                                    MessageId = message.MessageId,
                                    Type = messageType,
                                    AppId = this.parent.AppId,
                                    Exchange = Consts.RpcExchange,
                                    RoutingKey = message.RoutingKey,
                                    Body = result
                                };
                                await this.parent.rabbitProducer.Publish(Consts.RpcExchange, message.RoutingKey, rpcMessage.ToJson());
                            }
                            //RPC消息直接跳过，因为异常已经返回到前端了
                            if (!isSuccess && message.Type == MessageType.Message)
                                throw exception;
                        }
                        break;
                    case MessageType.WaitForStart:
                    case MessageType.WaitForShutdown:
                        Console.WriteLine($"{message.Type} - message: {this.QueueName} received!");
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
        }
        await channel.BasicConsumeAsync(this.QueueName, false, consumer);
    }
}