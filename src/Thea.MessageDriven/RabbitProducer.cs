using RabbitMQ.Client;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;

namespace Thea.MessageDriven;

class RabbitProducer : IDisposable
{
    private ConnectionFactory factory;
    private IConnection connection;
    private ConcurrentDictionary<int, Channel> channels = new();
    private BlockingCollection<Channel> channelQueue = new();
    private volatile Cluster clusterInfo;
    private readonly int channelSize = 10;

    public string ClusterId => this.clusterInfo?.ClusterId;
    public RabbitProducer(int channelSize = 10) => this.channelSize = channelSize;
    public RabbitProducer Create(Cluster clusterInfo)
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
                UseBackgroundThreadsForIO = true,
                AutomaticRecoveryEnabled = true,
                RequestedHeartbeat = TimeSpan.FromSeconds(10),
                NetworkRecoveryInterval = TimeSpan.FromSeconds(2),
                ClientProperties = new Dictionary<string, object>() {
                    { "connection_name", $"{clusterInfo.ClusterId}.producer" },
                    { "client_api", $"Thea.MessageDriven" }
                }
            };
            this.connection = this.factory.CreateConnection($"{clusterInfo.ClusterId}.producer");
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
    public void TryPublish(string exchange, string routingKey, string message)
    {
        var channel = this.channelQueue.Take();
        var body = Encoding.UTF8.GetBytes(message);
        channel.TryPublish(exchange, routingKey, body);
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
    public void CreateWorkerQueue(string clusterId, string bindType, string bindingKey, string queue)
    {
        //ring buffer环形无锁channel池
        var channel = this.channelQueue.Take();
        channel.CreateExchangeQueue(clusterId, bindType, bindingKey, queue);
        this.channelQueue.Add(channel);
    }
    public void CreateReplyQueue(string clusterId, string HostName)
    {
        var exchange = $"{clusterId}.result";
        var queue = $"{clusterId}.{HostName}.result";
        var channel = this.channelQueue.Take();
        channel.CreateExchangeQueue(exchange, "direct", HostName, queue);
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
    public void TryPublish(string exchange, string routingKey, byte[] message)
        => this.Model.BasicPublish(exchange, routingKey, this.Properties, message);
    public void CreateExchangeQueue(string exchange, string bindType, string bindingKey, string queue)
    {
        if (string.IsNullOrEmpty(bindType))
            bindType = "direct";
        this.Model.ExchangeDeclare(exchange, bindType, true, false);
        this.Model.QueueDeclare(queue, true, false, false);
        this.Model.QueueBind(queue, exchange, bindingKey);
    }
    public void Close() => this.Model.Close();
}
