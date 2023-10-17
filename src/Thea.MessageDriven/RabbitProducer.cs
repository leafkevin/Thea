using Microsoft.AspNetCore.Mvc.ModelBinding;
using RabbitMQ.Client;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using System.Threading.Channels;

namespace Thea.MessageDriven;

class RabbitProducer : IDisposable
{
    private ConnectionFactory factory;
    private IConnection connection;
    private ConcurrentDictionary<int, Channel> channels = new();
    private BlockingCollection<Channel> channelQueue = new();
    private volatile Cluster clusterInfo;
    private readonly int channelSize = 10;

    public RabbitProducer(int channelSize = 10) => this.channelSize = channelSize;
    public RabbitProducer Create(string producerName, Cluster clusterInfo)
    {
        if (this.clusterInfo == null || clusterInfo.Url != this.clusterInfo.Url
            || clusterInfo.User != this.clusterInfo.User || clusterInfo.Password != this.clusterInfo.Password)
        {
            if (this.connection != null)
                this.connection.Close();
            this.factory = new ConnectionFactory
            {
                Uri = new Uri(clusterInfo.Url),
                UserName = clusterInfo.User,
                Password = clusterInfo.Password,
                AutomaticRecoveryEnabled = true,
                RequestedHeartbeat = TimeSpan.FromSeconds(10),
                NetworkRecoveryInterval = TimeSpan.FromSeconds(2),
                ClientProperties = new Dictionary<string, object>() {
                    { "connection_name", producerName },
                    { "client_api", $"MessageDriven" }
                }
            };
            this.connection = this.factory.CreateConnection(producerName);
            for (int i = 0; i < channelSize; i++)
            {
                var channel = new Channel(this.connection);
                this.channels.TryAdd(i, channel);
            }
            this.AddChannelsToQueue();
            this.clusterInfo = clusterInfo;
        }
        return this;
    }
    public void Publish(string exchange, string routingKey, string message)
    {
        var channel = this.channelQueue.Take();
        var body = Encoding.UTF8.GetBytes(message);
        channel.Publish(exchange, routingKey, body);
        this.channelQueue.Add(channel);
    }
    public void Schedule(string exchange, string routingKey, DateTime scheduleTimeUtc, string message)
    {
        var channel = this.channelQueue.Take();
        var body = Encoding.UTF8.GetBytes(message);
        channel.Schedule(exchange, routingKey, scheduleTimeUtc, body);
        this.channelQueue.Add(channel);
    }
    public void Close()
    {
        if (this.channels != null && this.channels.Count > 0)
        {
            foreach (var channel in this.channels.Values)
                channel.Close();
            this.channels.Clear();
        }
        this.channels = null;
        if (this.channelQueue != null && this.channelQueue.Count > 0)
            while (this.channelQueue.TryTake(out _)) ;
        this.channelQueue = null;
        //等待本次消息消费结束 
        if (this.connection != null)
            this.connection.Close();
        this.connection = null;
    }
    public void CreateExchange(Cluster clusterInfo, string hostName)
    {
        //ring buffer环形无锁channel池
        var channel = this.channelQueue.Take();
        channel.CreateExchange(clusterInfo, hostName);
        this.channelQueue.Add(channel);
    }
    private void AddChannelsToQueue()
    {
        foreach (var channel in this.channels.Values)
        {
            this.channelQueue.Add(channel);
        }
    }
    public void Dispose() => this.Close();
}
class Channel
{
    private IBasicProperties Properties { get; set; }
    public IModel Model { get; set; }
    public Channel(IConnection connection)
    {
        this.Model = connection.CreateModel();
        this.Properties = this.Model.CreateBasicProperties();
        this.Properties.Persistent = true;
    }
    public void Publish(string exchange, string routingKey, byte[] message)
        => this.Model.BasicPublish(exchange, routingKey, this.Properties, message);
    public void Schedule(string exchange, string routingKey, DateTime scheduleTimeUtc, byte[] message)
    {
        var properties = this.Model.CreateBasicProperties();
        properties.Persistent = true;
        var delayMilliseconds = scheduleTimeUtc.Subtract(DateTime.UtcNow).TotalMilliseconds;
        properties.Headers = new Dictionary<string, object> { { "x-delay", (long)delayMilliseconds } };
        this.Model.BasicPublish(exchange + ".delay", routingKey, this.Properties, message);
    }
    public void CreateExchange(Cluster clusterInfo, string hostName)
    {
        var bindType = clusterInfo.BindType;
        if (string.IsNullOrEmpty(bindType))
            bindType = "direct";
        var exchange = clusterInfo.ClusterId;
        this.Model.ExchangeDeclare(exchange, bindType, true);

        if (clusterInfo.IsUseRpc)
        {
            exchange = $"{clusterInfo.ClusterId}.result";
            var queue = $"{clusterInfo.ClusterId}.{hostName}.result";
            this.Model.ExchangeDeclare(exchange, "direct", true);
            //应答队列暂时不做Single Active Consumer
            this.Model.QueueDeclare(queue, true, false, false);
            this.Model.QueueBind(queue, exchange, hostName);
        }

        if (clusterInfo.IsUseDelay)
        {
            exchange = clusterInfo.ClusterId + ".delay";
            bindType = "x-delayed-message";
            var exchangeArguments = new Dictionary<string, object> { { "x-delayed-type", "direct" } };
            this.Model.ExchangeDeclare(exchange, bindType, true, false, exchangeArguments);
        }
    }
    //public void CreateExchangeQueue(Cluster clusterInfo, Binding bindingInfo, string hostName)
    //{
    //    var bindType = clusterInfo.BindType;
    //    if (string.IsNullOrEmpty(bindType))
    //        bindType = "direct";
    //    var exchange = clusterInfo.ClusterId;
    //    this.Model.ExchangeDeclare(exchange, bindType, true);

    //    IDictionary<string, object> queueArguments = null;
    //    if (bindingInfo.IsSingleActiveConsumer)
    //        queueArguments = new Dictionary<string, object> { { "x-single-active-consumer", true } };
    //    this.Model.QueueDeclare(bindingInfo.Queue, true, false, false, queueArguments);
    //    this.Model.QueueBind(bindingInfo.Queue, clusterInfo.ClusterId, bindingInfo.BindingKey);

    //    if (bindingInfo.IsReply)
    //    {
    //        exchange = $"{clusterInfo.ClusterId}.result";
    //        var queue = $"{clusterInfo.ClusterId}.{hostName}.result";
    //        this.Model.ExchangeDeclare(exchange, "direct", true);
    //        //应答队列暂时不做Single Active Consumer
    //        this.Model.QueueDeclare(queue, true, false, false);
    //        this.Model.QueueBind(queue, exchange, hostName);
    //    }

    //    if (clusterInfo.IsUseDelay)
    //    {
    //        exchange = clusterInfo.ClusterId + ".delay";
    //        bindType = "x-delayed-message";
    //        var exchangeArguments = new Dictionary<string, object> { { "x-delayed-type", "direct" } };
    //        this.Model.ExchangeDeclare(exchange, bindType, true, false, exchangeArguments);
    //        this.Model.QueueBind(bindingInfo.Queue, exchange, bindingInfo.BindingKey);
    //    }

    //    IDictionary<string, object> arguments = null;
    //    if (clusterInfo.IsUseDelay)
    //    {
    //        bindType = "x-delayed-message";
    //        arguments = new Dictionary<string, object> { { "x-delayed-type", "direct" } };
    //    }
    //    this.Model.ExchangeDeclare(exchange, bindType, true, false, arguments);
    //}
    public void Close() => this.Model.Close();
}
