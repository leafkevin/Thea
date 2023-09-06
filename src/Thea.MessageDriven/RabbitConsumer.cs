using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Thea.MessageDriven;

class RabbitConsumer
{
    private ConnectionFactory factory;
    private Func<TheaMessage, Task<TheaResponse>> consumerHandler;
    private Action<TheaMessage> nextHandler;
    private ClusterRepository repository;
    private readonly ushort prefetchCount = 5;
    private readonly ILogger<RabbitConsumer> logger;
    private readonly string nodeId;
    private string consumerId;
    private volatile bool isNeedBuiding;
    private volatile IConnection connection = null;
    private volatile IModel channel = null;
    private volatile Cluster clusterInfo;
    private volatile Binding bindingInfo;

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
        this.nodeId = parent.NodeId;
        this.repository = parent.repository;
        this.logger = serviceProvider.GetService<ILogger<RabbitConsumer>>();
        this.nextHandler = parent.Next;
    }
    public RabbitConsumer(MessageDrivenService parent, IServiceProvider serviceProvider, Func<TheaMessage, Task<TheaResponse>> consumerHandler)
        : this(parent, serviceProvider)
    {
        this.consumerHandler = consumerHandler;
    }
    public RabbitConsumer Bind(string consumerId, Cluster clusterInfo)
    {
        this.consumerId = consumerId;
        if (this.clusterInfo == null || clusterInfo.UpdatedAt > this.clusterInfo.UpdatedAt)
        {
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
            this.Shutdown();
            this.clusterInfo = clusterInfo;
            this.isNeedBuiding = true;
        }
        return this;
    }
    public RabbitConsumer Bind(string consumerId, Cluster clusterInfo, Binding bindingInfo)
    {
        this.consumerId = consumerId;
        if (this.clusterInfo == null || clusterInfo.UpdatedAt > this.clusterInfo.UpdatedAt)
        {
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
            this.Shutdown();
            this.clusterInfo = clusterInfo;
            this.isNeedBuiding = true;
        }
        if (this.bindingInfo == null || bindingInfo.UpdatedAt > this.bindingInfo.UpdatedAt)
        {
            this.bindingInfo = bindingInfo;
            this.isNeedBuiding = true;
        }
        return this;
    }
    public void CreateWorkerQueue(string clusterId, string bindType, string bindingKey, string queue)
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
        this.channel.QueueDeclare(queue, true, false, false, null);
        this.channel.QueueBind(queue, clusterId, bindingKey, null);
        this.isNeedBuiding = false;
    }
    public void CreateReplyQueue(string clusterId, string nodeId)
    {
        if (!this.isNeedBuiding) return;
        var exchange = $"{clusterId}.result";
        var queue = $"{clusterId}.{nodeId}.result";
        this.channel.ExchangeDeclare(exchange, "direct", true);
        this.channel.QueueDeclare(queue, true, false, false, null);
        this.channel.QueueBind(queue, exchange, nodeId, null);
        this.isNeedBuiding = false;
    }
    public void Start()
    {
        this.connection = this.factory.CreateConnection();
        this.channel = this.connection.CreateModel();

        if (this.bindingInfo.IsReply)
            this.CreateReplyQueue(this.clusterInfo.ClusterId, this.nodeId);
        else this.CreateWorkerQueue(this.clusterInfo.ClusterId, this.clusterInfo.BindType, this.bindingInfo.BindingKey, this.bindingInfo.Queue);

        this.channel.BasicQos(0, this.prefetchCount, false);
        this.channel.BasicRecoverOk += (o, e) =>
        {
            var model = o as IModel;
            //其他的Consumer,客户端已提供恢复了
            model.BasicQos(0, this.prefetchCount, false);
        };
        this.BindConsumer(this.channel);
    }
    public void Shutdown()
    {
        if (this.channel != null)
            channel.Close();
        //等待本次消息消费结束 
        if (this.connection != null)
            this.connection.Close();
        this.connection = null;
    }
    public void EnsureAvailable()
    {
        if (this.IsAvailable) return;
        this.Shutdown();
        this.Start();
    }
    private void BindConsumer(IModel channel)
    {
        var consumer = new EventingBasicConsumer(channel);
        consumer.Received += async (model, ea) =>
        {
            var iLoop = 0;
            TheaResponse resp = null;
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
                    resp = await this.consumerHandler(message);
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

            var result = isSuccess ? resp.ToJson() : $"Message:{jsonBody},Exception:{exception}";
            var logInfo = new ExecLog
            {
                LogId = ObjectId.NewId(),
                ClusterId = message.ClusterId,
                RoutingKey = ea.RoutingKey,
                Body = jsonBody,
                IsSuccess = isSuccess,
                Result = result,
                RetryTimes = iLoop - 1,
                CreatedAt = DateTime.Now,
                CreatedBy = this.consumerId,
                UpdatedAt = DateTime.Now,
                UpdatedBy = $"{this.Queue}-{this.consumerId}"
            };
            await this.repository.AddLog(logInfo);
            if (!isSuccess) this.logger.LogError($"Consume message failed, Detail:{logInfo.ToJson()}");
            if (message.Status != MessageStatus.None)
            {
                message.Message = resp.ToJson();
                message.Status = message.Status + 1;
                this.nextHandler(message);
            }
            channel.BasicAck(ea.DeliveryTag, false);
        };
        channel.BasicConsume(this.Queue, false, consumer);
    }
}
