using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Thea.Logging;

namespace Thea.MessageDriven;

class RabbitConsumer
{
    private ConnectionFactory factory;
    private Func<string, Task<object>> consumerHandler;
    private readonly Action<TheaMessage, Exception> nextHandler;
    private Action<ExecLog> addLogsHandler;
    private readonly ushort prefetchCount = 200;
    private readonly ILogger<RabbitConsumer> logger;
    private readonly string HostName;
    private string consumerId;
    private volatile bool isNeedBuiding;
    private volatile IConnection connection = null;
    private volatile IModel channel = null;
    private volatile Cluster clusterInfo;
    private volatile Binding bindingInfo;
    private DateTime updatedAt;
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
        this.connection = this.factory.CreateConnection();
        this.channel = this.connection.CreateModel();

        if (this.bindingInfo.IsReply)
            this.CreateReplyQueue(this.clusterInfo.ClusterId, this.HostName);
        else this.CreateWorkerQueue(this.clusterInfo.ClusterId, this.clusterInfo.BindType, this.bindingInfo.BindingKey, this.bindingInfo.Queue);

        this.channel.BasicQos(0, this.prefetchCount, false);
        this.channel.BasicRecoverOk += (o, e) =>
        {
            var model = o as IModel;
            model.BasicQos(0, this.prefetchCount, false);
        };
        this.BindHandler(this.channel, this.bindingInfo.Queue);
        this.isNeedBuiding = false;
    }
    public void Remove()
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
            || bindingInfo.Exchange != this.bindingInfo.Exchange || bindingInfo.IsReply != this.bindingInfo.IsReply)
        {
            this.Shutdown();
            this.factory = new ConnectionFactory
            {
                Uri = new Uri(clusterInfo.Url),
                UserName = clusterInfo.User,
                Password = clusterInfo.Password,
                UseBackgroundThreadsForIO = true,
                AutomaticRecoveryEnabled = true,
                RequestedHeartbeat = TimeSpan.FromSeconds(10),
                NetworkRecoveryInterval = TimeSpan.FromSeconds(2)
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
        return this;
    }
    private void CreateWorkerQueue(string clusterId, string bindType, string bindingKey, string queue)
    {
        if (!this.isNeedBuiding) return;
        if (string.IsNullOrEmpty(bindType))
            bindType = "direct";

        var exchange = bindType.ToLower() switch
        {
            "fanout" => bindType + ".fanout",
            "topic" => bindType + ".topic",
            _ => clusterId
        };
        this.channel.ExchangeDeclare(exchange, bindType, true);
        this.channel.QueueDeclare(queue, true, false, false);
        this.channel.QueueBind(queue, clusterId, bindingKey);
    }
    private void CreateReplyQueue(string clusterId, string hostName)
    {
        if (!this.isNeedBuiding) return;
        var exchange = $"{clusterId}.result";
        var queue = $"{clusterId}.{hostName}.result";
        this.channel.ExchangeDeclare(exchange, "direct", true);
        this.channel.QueueDeclare(queue, true, false, false);
        this.channel.QueueBind(queue, exchange, hostName);
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
            message.Queue = this.Queue;

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
                    exception = ex;
                    resp = TheaResponse.Fail(-1, ex.ToString(), ex);
                }
                iLoop++;
                Thread.Sleep(1000);
            }
            var result = isSuccess ? resp.ToJson() : exception.ToString();
            var logInfo = new ExecLog
            {
                LogId = ObjectId.NewId(),
                ClusterId = message.ClusterId,
                RoutingKey = ea.RoutingKey,
                Body = jsonBody,
                IsSuccess = isSuccess,
                Result = result,
                RetryTimes = iLoop,
                CreatedAt = DateTime.Now,
                CreatedBy = this.consumerId,
                UpdatedAt = DateTime.Now,
                UpdatedBy = this.consumerId
            };
            this.addLogsHandler.Invoke(logInfo);
            if (!isSuccess) this.logger.LogTagError("RabbitConsumer", $"Consume message failed, Detail:{logInfo.ToJson()}");
            if (message.Status == MessageStatus.WaitForReply)
            {
                message.Message = resp.ToJson();
                message.Status = isSuccess ? MessageStatus.SetResult : MessageStatus.SetException;
                this.nextHandler.Invoke(message, exception);
            }
            channel.BasicAck(ea.DeliveryTag, false);
        };
        channel.BasicConsume(queue, false, consumer);
    }
}
