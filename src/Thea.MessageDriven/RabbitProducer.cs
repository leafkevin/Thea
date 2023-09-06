using RabbitMQ.Client;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Thea.MessageDriven;

class RabbitProducer
{
    private ConnectionFactory factory;
    private IConnection connection;
    private Dictionary<int, Channel> channelList = new();
    private BlockingCollection<Channel> channelQueue = new();
    private volatile Cluster clusterInfo;
    private readonly int channelSize = 10;

    public string ClusterId { get; private set; }
    public RabbitProducer(string clusterId, int channelSize = 10)
    {
        this.ClusterId = clusterId;
        this.channelSize = channelSize;
    }
    public void Create(Cluster clusterInfo)
    {
        if (this.clusterInfo == null || clusterInfo.UpdatedAt > this.clusterInfo.UpdatedAt)
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
                NetworkRecoveryInterval = TimeSpan.FromSeconds(2)
            };
            this.connection = this.factory.CreateConnection();
            for (int i = 0; i < channelSize; i++)
            {
                var channel = new Channel(this.connection);
                this.channelList.Add(i, channel);
            }
            this.AddChannelsToQueue();
        }
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
        var keyList = this.channelList.Keys.ToList();
        for (int i = 0; i < keyList.Count; i++)
        {
            if (this.channelList.TryGetValue(keyList[i], out var channel))
            {
                channel.Close();
            }
        }
        this.channelList.Clear();
        //等待本次消息消费结束 
        if (this.connection != null)
            this.connection.Close();
        this.connection = null;
    }
    private void AddChannelsToQueue()
    {
        foreach (var channel in this.channelList.Values)
        {
            this.channelQueue.Add(channel);
        }
    }
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
    public void Close() => this.Model.Close();
}
