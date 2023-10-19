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
        if (this.connection != null)
            this.connection.Close();
        this.connection = null;
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
        if (!exchange.EndsWith(".delay"))
            exchange += ".delay";
        this.Model.BasicPublish(exchange, routingKey, properties, message);
    }
    public void Close() => this.Model.Close();
}
