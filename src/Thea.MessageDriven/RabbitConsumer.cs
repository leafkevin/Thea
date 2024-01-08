using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Thea.Logging;

namespace Thea.MessageDriven;

class RabbitConsumer
{
    private ConnectionFactory factory;
    private Func<string, Task<object>> consumerHandler;
    private readonly Action<TheaMessage, MessageStatus> nextHandler;
    private Action<ExecLog> addLogsHandler;
    private readonly ILogger<RabbitConsumer> logger;
    private readonly string HostName;
    private string consumerId;
    private volatile bool isNeedBuiding;
    private volatile IConnection connection = null;
    private volatile IModel channel = null;
    private volatile Cluster clusterInfo;
    private volatile Binding bindingInfo;
    private volatile bool isLogEnabled = false;
    public string Queue { get; set; }

    public bool IsAvailable
    {
        get
        {
            if (this.connection == null) return false;
            if (!this.connection.IsOpen) return false;
            if (this.channel != null && this.channel.IsClosed)
                return false;
            return true;
        }
    }
    public RabbitConsumer(MessageDrivenService parent, IServiceProvider serviceProvider)
    {
        this.HostName = parent.HostName;
        this.addLogsHandler = parent.AddLogs;
        this.logger = serviceProvider.GetService<ILogger<RabbitConsumer>>();
        this.nextHandler = parent.Next;
    }
    public RabbitConsumer(MessageDrivenService parent, IServiceProvider serviceProvider, Func<string, Task<object>> consumerHandler)
        : this(parent, serviceProvider)
    {
        this.consumerHandler = consumerHandler;
    }
    public void Build(string consumerId, Cluster clusterInfo, Binding bindingInfo)
        => this.Bind(consumerId, clusterInfo, bindingInfo).Start();
    public void Start()
    {
        if (this.factory == null || !this.isNeedBuiding) return;
        this.connection = this.factory.CreateConnection(this.consumerId);
        this.channel = this.connection.CreateModel();
        this.CreateExchangeQueue();
        this.channel.BasicQos(0, (ushort)this.bindingInfo.PrefetchCount, false);
        this.channel.BasicRecoverOk += (o, e) =>
        {
            var model = o as IModel;
            model.BasicQos(0, (ushort)this.bindingInfo.PrefetchCount, false);
        };
        this.BindHandler(this.channel, this.bindingInfo.Queue);
        this.isNeedBuiding = false;
    }
    public void RemoveQueue()
    {
        if (this.channel != null)
            channel.QueueDelete(this.Queue);
    }
    public void Shutdown()
    {
        if (this.channel != null)
        {
            channel.Close();
            this.channel = null;
            this.isNeedBuiding = true;
        }
        if (this.connection != null)
        {
            this.connection.Close();
            this.connection = null;
            this.isNeedBuiding = true;
        }
    }
    public void EnsureAvailable()
    {
        if (this.IsAvailable) return;
        this.Shutdown();
        this.Start();
    }
    private RabbitConsumer Bind(string consumerId, Cluster clusterInfo, Binding bindingInfo)
    {
        this.consumerId = consumerId;
        if (this.isNeedBuiding || this.bindingInfo == null || bindingInfo.BindType != this.bindingInfo.BindType
            || bindingInfo.BindingKey != this.bindingInfo.BindingKey || bindingInfo.Queue != this.bindingInfo.Queue
            || bindingInfo.Exchange != this.bindingInfo.Exchange || bindingInfo.IsReply != this.bindingInfo.IsReply
            || bindingInfo.PrefetchCount != this.bindingInfo.PrefetchCount || bindingInfo.IsSingleActiveConsumer != this.bindingInfo.IsSingleActiveConsumer)
        {
            this.Shutdown();
            this.factory = new ConnectionFactory
            {
                Uri = new Uri(clusterInfo.Url),
                UserName = clusterInfo.User,
                Password = clusterInfo.Password,
                AutomaticRecoveryEnabled = true,
                RequestedHeartbeat = TimeSpan.FromSeconds(10),
                NetworkRecoveryInterval = TimeSpan.FromSeconds(2),
                ClientProperties = new Dictionary<string, object>() {
                    { "connection_name", consumerId },
                    { "client_api", $"Thea.MessageDriven" }
                }
            };
            this.clusterInfo = clusterInfo;
            this.isNeedBuiding = true;
        }
        if (this.isNeedBuiding || this.bindingInfo == null || bindingInfo.BindType != this.bindingInfo.BindType
           || bindingInfo.BindingKey != this.bindingInfo.BindingKey || bindingInfo.Queue != this.bindingInfo.Queue
           || bindingInfo.Exchange != this.bindingInfo.Exchange || bindingInfo.IsReply != this.bindingInfo.IsReply)
        {
            this.bindingInfo = bindingInfo;
            this.Queue = this.bindingInfo.Queue;
            this.isNeedBuiding = true;
        }
        if (clusterInfo != null)
            this.isLogEnabled = clusterInfo.IsLogEnabled;
        return this;
    }
    private void CreateExchangeQueue()
    {
        if (!this.isNeedBuiding) return;

        var bindType = this.bindingInfo.BindType;
        if (string.IsNullOrEmpty(bindType))
            bindType = "topic";
        var exchange = this.bindingInfo.Exchange;
        Dictionary<string, object> exchangeArguments = null;
        if (this.bindingInfo.IsDelay)
            exchangeArguments = new Dictionary<string, object> { { "x-delayed-type", "topic" } };
        this.channel.ExchangeDeclare(exchange, bindType, true, false, exchangeArguments);

        IDictionary<string, object> queueArguments = null;
        if (this.bindingInfo.IsSingleActiveConsumer)
            queueArguments = new Dictionary<string, object> { { "x-single-active-consumer", true } };
        this.channel.QueueDeclare(this.Queue, true, false, false, queueArguments);
        this.channel.QueueBind(this.Queue, exchange, this.bindingInfo.BindingKey);
    }
    private void BindHandler(IModel channel, string queue)
    {
        var consumer = new EventingBasicConsumer(channel);
        consumer.Received += async (model, ea) =>
        {
            var iLoop = 0;
            object resp = null;
            Exception exception = null;
            string jsonBody = null;
            TheaMessage message = null;
            bool isSuccess = true;

            jsonBody = Encoding.UTF8.GetString(ea.Body.Span);
            message = jsonBody.JsonTo<TheaMessage>();
            //兼容现非框架队列消息
            if (message.Message == null)
            {
                message.MessageId ??= ObjectId.NewId();
                message.Message = jsonBody;
            }
            if (!string.IsNullOrEmpty(message.ReplyExchange) && message.Status.HasValue
                && message.Status > MessageStatus.WaitForReply)
                this.nextHandler.Invoke(message, message.Status.Value);
            else
            {
                while (iLoop < 3)
                {
                    try
                    {
                        resp = await this.consumerHandler.Invoke(message.Message);
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

                var result = isSuccess ? resp.ToJson() : exception.ToString();
                var logInfo = new ExecLog
                {
                    LogId = ObjectId.NewId(),
                    ClusterId = this.clusterInfo.ClusterId,
                    RoutingKey = this.bindingInfo.BindingKey,
                    Queue = this.Queue,
                    Body = jsonBody,
                    IsSuccess = isSuccess,
                    Result = result,
                    RetryTimes = iLoop,
                    CreatedAt = DateTime.Now,
                    CreatedBy = this.consumerId,
                    UpdatedAt = DateTime.Now,
                    UpdatedBy = this.consumerId
                };
                if (this.isLogEnabled || !isSuccess)
                {
                    this.addLogsHandler.Invoke(logInfo);
                    if (!isSuccess) this.logger.LogTagError("RabbitConsumer", exception, $"Consume message failed, Message:{jsonBody}");
                }
                if (!string.IsNullOrEmpty(message.ReplyExchange) && message.Status.HasValue)
                {
                    message.Message = result;
                    MessageStatus nextStatus = default;
                    if (message.Status.Value == MessageStatus.WaitForReply)
                        nextStatus = isSuccess ? MessageStatus.SetResult : MessageStatus.SetException;
                    this.nextHandler.Invoke(message, nextStatus);
                }
            }
            channel.BasicAck(ea.DeliveryTag, false);
        };
        channel.BasicConsume(queue, false, consumer);
    }
}
