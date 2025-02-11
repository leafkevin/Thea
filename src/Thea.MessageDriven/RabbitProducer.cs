using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RabbitMQ.Client;

namespace Thea.MessageDriven;

class RabbitProducer : IDisposable
{
    private ConnectionFactory factory;
    private IConnection connection;
    private ConcurrentDictionary<int, Channel> channels = new();
    private BlockingCollection<Channel> channelQueue = new();
    private int channelSize = 10;

    public RabbitProducer(MessageDrivenService parent, IServiceProvider serviceProvider, int channelSize = 10)
    {
        var hostName = parent.NodeId;
        this.channelSize = channelSize;
        var configuration = serviceProvider.GetService<IConfiguration>();
        var url = configuration.GetValue<string>("MessageDriven:Url");
        var user = configuration.GetValue<string>("MessageDriven:User");
        var password = configuration.GetValue<string>("MessageDriven:Password");

        this.factory = new ConnectionFactory
        {
            Uri = new Uri(url),
            UserName = user,
            Password = password,
            AutomaticRecoveryEnabled = true,
            RequestedHeartbeat = TimeSpan.FromSeconds(10),
            NetworkRecoveryInterval = TimeSpan.FromSeconds(2),
            ClientProperties = new Dictionary<string, object>()
            {
                { "connection_name", $"producer-{hostName}" },
                { "client_api", $"Thea.MessageDriven" }
            }
        };
        this.connection = this.factory.CreateConnection(hostName);
        for (int i = 0; i < channelSize; i++)
        {
            var channel = new Channel(this.connection);
            this.channels.TryAdd(i, channel);
            this.channelQueue.Add(channel);
        }
    }
    public void CreateExchange(string exchangeName, string bindType, bool isDelay = false)
    {
        var channel = this.channelQueue.Take();
        channel.CreateExchange(exchangeName, bindType, isDelay);
        this.channelQueue.Add(channel);
    }
    public void CreateQueue(string queueName, bool isSac = false)
    {
        var channel = this.channelQueue.Take();
        channel.CreateQueue(queueName, isSac);
        this.channelQueue.Add(channel);
    }
    public void BindQueue(string exchange, string queueName, string bindingKey)
    {
        var channel = this.channelQueue.Take();
        channel.BindQueue(exchange, queueName, bindingKey);
        this.channelQueue.Add(channel);
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
    public void Shutdown()
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
    public void Dispose() => this.Shutdown();
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
    public void CreateExchange(string exchangeName, string bindType, bool isDelay)
    {
        Dictionary<string, object> arguments = null;
        if (isDelay) arguments = new Dictionary<string, object> { { "x-delayed-type", "topic" } };
        this.Model.ExchangeDeclare(exchangeName, bindType, true, false, arguments);
    }
    public void CreateQueue(string queueName, bool isSac)
    {
        IDictionary<string, object> arguments = null;
        if (isSac) arguments = new Dictionary<string, object> { { "x-single-active-consumer", true } };
        this.Model.QueueDeclare(queueName, true, false, false, arguments);
    }
    public void BindQueue(string exchange, string queueName, string bindingKey)
        => this.Model.QueueBind(queueName, exchange, bindingKey);
    public void Publish(string exchange, string routingKey, byte[] message)
        => this.Model.BasicPublish(exchange, routingKey, this.Properties, message);
    public void Schedule(string exchange, string routingKey, DateTime scheduleTimeUtc, byte[] message)
    {
        var properties = this.Model.CreateBasicProperties();
        properties.Persistent = true;
        var delayMilliseconds = scheduleTimeUtc.Subtract(DateTime.UtcNow).TotalMilliseconds;
        properties.Headers = new Dictionary<string, object> { { "x-delay", (long)delayMilliseconds } };
        this.Model.BasicPublish(exchange, routingKey, properties, message);
    }
    public void Close() => this.Model.Close();
}