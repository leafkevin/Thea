using System;
using System.Collections.Generic;
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
    private volatile IConnection connection = null;
    private volatile IChannel channel = null;
    private string connectionId;
    private bool isExclusive = false;
    private bool isRpc = false;
    private Type messageType;
    private Delegate consumerHandler;

    public volatile bool IsRunning = false;
    public volatile bool IsStarted = false;
    private volatile bool isDeferClose = false;
    public volatile bool IsLogEnabled;
    public string ClusterId { get; private set; }
    public string ConsumerId { get; private set; }
    public string QueueName { get; private set; }

    public RabbitConsumer(string clusterId, string queueName, MessageDrivenService parent, IServiceProvider serviceProvider, bool isExclusive, MethodInfo consumerInvoker = null)
    {
        this.parent = parent;
        this.ClusterId = clusterId;
        this.ConsumerId = ObjectId.NewId();
        this.QueueName = queueName;
        this.connectionId = $"{clusterId}-{queueName}-{parent.NodeId}";
        this.addLogsHandler = parent.AddLogs;
        this.logger = serviceProvider.GetService<ILogger<RabbitConsumer>>();
        var configuration = serviceProvider.GetService<IConfiguration>();
        var url = configuration.GetValue<string>("MessageDriven:Url");
        var user = configuration.GetValue<string>("MessageDriven:User");
        var password = configuration.GetValue<string>("MessageDriven:Password");
        this.isExclusive = isExclusive;
        this.isRpc = parent.RpcClusterIds.Contains(clusterId);
        if (consumerInvoker != null)
        {
            var targetType = consumerInvoker.DeclaringType;
            var target = serviceProvider.GetService(targetType);
            var methodExecutor = ObjectMethodExecutor.Create(consumerInvoker, targetType.GetTypeInfo());
            this.messageType = consumerInvoker.GetParameters().FirstOrDefault().ParameterType;
            if (this.isRpc)
            {
                Func<object, Task<object>> consumerHandler = methodExecutor.IsMethodAsync ? async message =>
                    await methodExecutor.ExecuteAsync(target, [message]) : message => Task.FromResult(methodExecutor.Execute(target, [message]));
                this.consumerHandler = consumerHandler;
            }
            else
            {
                Func<object, Task> consumerHandler = methodExecutor.IsMethodAsync ? async message =>
                    await methodExecutor.ExecuteAsync(target, [message]) : message => { methodExecutor.Execute(target, [message]); return Task.CompletedTask; };
                this.consumerHandler = consumerHandler;
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
            await channel.QueueDeleteAsync(this.QueueName);
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
            switch (message.Type)
            {
                case MessageType.Message:
                    while (iLoop < 3)
                    {
                        try
                        {
                            var parameters = TheaJsonSerializer.Deserialize(message.Body, this.messageType);
                            if (this.isRpc)
                            {
                                var handler = (Func<object, Task<object>>)this.consumerHandler;
                                var rpcResult = await handler.Invoke(parameters);
                                var rpcMessage = new Message
                                {
                                    MessageId = message.MessageId,
                                    Type = MessageType.RpcMessage,
                                    AppId = this.parent.AppId,
                                    Body = rpcResult.ToJson()
                                };
                                await this.parent.rabbitProducer.Publish("rpc.result", this.parent.NodeId, rpcMessage.ToJson());
                            }
                            else
                            {
                                var handler = (Func<object, Task>)this.consumerHandler;
                                await handler.Invoke(parameters);
                            }
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

                    var result = isSuccess ? "success" : exception.ToString();
                    var logInfo = new ExecLog
                    {
                        LogId = ObjectId.NewId(),
                        ClusterId = this.ClusterId,
                        RoutingKey = ea.RoutingKey,
                        Queue = this.QueueName,
                        Body = jsonBody,
                        IsSuccess = isSuccess,
                        Result = result,
                        RetryTimes = iLoop,
                        UpdatedAt = DateTime.Now,
                        UpdatedBy = this.ConsumerId
                    };
                    if (this.IsLogEnabled || !isSuccess)
                    {
                        this.addLogsHandler.Invoke(logInfo);
                        if (!isSuccess) this.logger.LogTagError("RabbitConsumer", exception, $"Consume message failed, Message:{jsonBody}");
                    }
                    if (!isSuccess) throw exception;
                    break;
                case MessageType.RpcMessage:
                    this.parent.Next(message.MessageId, message.Body);
                    break;
                default:
                    if (message.AppId == this.parent.AppId)
                    {
                        var body = message.Body.ToString();
                        var waiter = new TaskCompletionSource<bool>();
                        this.parent.ProcessMessage(new Message
                        {
                            MessageId = message.MessageId,
                            Type = message.Type,
                            Body = (body, waiter)
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