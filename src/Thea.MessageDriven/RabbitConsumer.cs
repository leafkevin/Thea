using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
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
    private ConnectionFactory factory;
    private Action<ExecLog> addLogsHandler;
    private readonly ILogger<RabbitConsumer> logger;
    private IConnection connection = null;
    private IChannel channel = null;
    private string connectionId;
    private bool isExclusive = false;
    private Dictionary<string, (Type, Type, Func<object, Task<object>>)> exchangeHandlers;

    public volatile bool IsRunning = false;
    public volatile bool IsStarted = false;
    private volatile bool isDeferClose = false;
    public volatile bool IsLogEnabled;
    public string ConsumerId { get; private set; }
    public string QueueName { get; private set; }

    public RabbitConsumer(string queueName, MessageDrivenService parent, IServiceProvider serviceProvider, bool isExclusive, Dictionary<string, MethodInfo> exchangeMethodInfos = null)
    {
        this.parent = parent;
        this.ConsumerId = ObjectId.NewId();
        this.QueueName = queueName;
        this.connectionId = queueName;
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
                var isVoid = consumerInvoker.ReturnType == typeof(void) || consumerInvoker.ReturnType == typeof(Task);
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
        if (this.IsRunning || this.IsStarted) return;
        this.connection = await this.factory.CreateConnectionAsync(this.connectionId);
        this.channel = await this.connection.CreateChannelAsync();
        ushort prefetchCount = 20;
        await this.channel.BasicQosAsync(0, prefetchCount, false);
        //this.channel.BasicRecoverOk += (o, e) =>
        //{
        //    var model = o as IModel;
        //    model.BasicQos(0, prefetchCount, false);
        //};
        await this.BindHandler(this.channel);
        this.IsStarted = true;
    }
    public async Task Start(string exclusiveExchange, string exclusiveBindingKey)
    {
        if (this.IsRunning || this.IsStarted) return;
        this.connection = await this.factory.CreateConnectionAsync(this.connectionId);
        this.channel = await this.connection.CreateChannelAsync();
        if (this.isExclusive)
        {
            await this.channel.QueueDeclareAsync(this.QueueName, false, true, false);
            await this.channel.QueueBindAsync(this.QueueName, exclusiveExchange, exclusiveBindingKey);
        }

        ushort prefetchCount = 20;
        await this.channel.BasicQosAsync(0, prefetchCount, false);
        //this.channel.BasicRecoverOk += (o, e) =>
        //{
        //    var model = o as IModel;
        //    model.BasicQos(0, prefetchCount, false);
        //};
        await this.BindHandler(this.channel);
        this.IsStarted = true;
    }
    public async Task RemoveQueue()
    {
        if (this.channel != null)
            await this.channel.QueueDeleteAsync(this.QueueName);
    }
    public async void Shutdown()
    {
        this.cancellationSource.Cancel();
        if (this.IsRunning) this.isDeferClose = true;
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
    private async Task BindHandler(IChannel channel)
    {
        var consumer = new AsyncEventingBasicConsumer(channel);
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
            string result = "success";
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
                                    var rpcResult = await typedHandler.Invoke(parameters);
                                    var rpcMessage = new Message
                                    {
                                        MessageId = message.MessageId,
                                        Type = MessageType.RpcResponse,
                                        AppId = this.parent.AppId,
                                        Body = rpcResult.ToJson()
                                    };
                                    result += ", " + rpcResult.ToJson();
                                    await this.parent.rabbitProducer.Publish("rpc.result", message.RoutingKey, rpcMessage.ToJson());
                                    break;
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
                        if (!isSuccess) throw exception;
                    }
                    break;
                case MessageType.RpcResponse:
                    this.parent.Next(message.MessageId, message.Body);
                    break;
                case MessageType.Heartbeat:
                    if (message.AppId == this.parent.AppId)
                    {
                        var waiter = new TaskCompletionSource<bool>();
                        this.parent.ProcessMessage(new Message
                        {
                            MessageId = message.MessageId,
                            Type = message.Type,
                            Body = (message.Body, waiter)
                        });
                        waiter.Task.Wait();
                    }
                    break;
                case MessageType.WaitForStart:
                    break;
                case MessageType.WaitForShutdown:
                    await this.parent.rabbitProducer.Publish("heartbeat", "#", message.ToJson());
                    break;
                default:
                    if (message.AppId == this.parent.AppId)
                    {
                        var waiter = new TaskCompletionSource<bool>();
                        this.parent.ProcessMessage(new Message
                        {
                            MessageId = message.MessageId,
                            Type = message.Type,
                            Body = (message.Body, waiter)
                        });
                        waiter.Task.Wait();
                    }
                    break;
            }
            await channel.BasicAckAsync(ea.DeliveryTag, false);

            //再延迟停止
            if (this.isDeferClose)
                await this.Close();
        };
        await channel.BasicConsumeAsync(this.QueueName, false, consumer);
    }
}