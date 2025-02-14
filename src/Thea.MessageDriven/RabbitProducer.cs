using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RabbitMQ.Client;

namespace Thea.MessageDriven;

class RabbitProducer : IDisposable
{
    private IConnection connection;
    private ConcurrentDictionary<int, ProducerChannel> channels;
    private BlockingCollection<ProducerChannel> channelQueue;

    public static async Task<RabbitProducer> Create(MessageDrivenService parent, IServiceProvider serviceProvider, int channelSize = 10)
    {
        var connectionId = $"producer-{parent.NodeId}";
        var configuration = serviceProvider.GetService<IConfiguration>();
        var url = configuration.GetValue<string>("MessageDriven:Url");
        var user = configuration.GetValue<string>("MessageDriven:User");
        var password = configuration.GetValue<string>("MessageDriven:Password");

        var factory = new ConnectionFactory
        {
            Uri = new Uri(url),
            UserName = user,
            Password = password,
            AutomaticRecoveryEnabled = true,
            RequestedHeartbeat = TimeSpan.FromSeconds(10),
            NetworkRecoveryInterval = TimeSpan.FromSeconds(2),
            ClientProperties = new Dictionary<string, object>()
            {
                { "connection_name",  connectionId},
                { "client_api", $"Thea.MessageDriven" }
            }
        };
        var connection = await factory.CreateConnectionAsync(connectionId);
        var channels = new ConcurrentDictionary<int, ProducerChannel>();
        var channelQueue = new BlockingCollection<ProducerChannel>();
        for (int i = 0; i < channelSize; i++)
        {
            var channel = await ProducerChannel.Create(connection);
            channels.TryAdd(i, channel);
            channelQueue.Add(channel);
        }
        return new RabbitProducer
        {
            connection = connection,
            channels = channels,
            channelQueue = channelQueue
        };
    }
    public async Task CreateExchange(string exchangeName, string bindType, bool isDelay = false)
    {
        var channel = this.channelQueue.Take();
        await channel.CreateExchange(exchangeName, bindType, isDelay);
        this.channelQueue.Add(channel);
    }
    public async Task CreateQueue(string queueName, bool isSac, bool isHeartbeat)
    {
        var channel = this.channelQueue.Take();
        await channel.CreateQueue(queueName, isSac, isHeartbeat);
        this.channelQueue.Add(channel);
    }
    public async Task BindQueue(string exchange, string queueName, string bindingKey)
    {
        var channel = this.channelQueue.Take();
        await channel.BindQueue(exchange, queueName, bindingKey);
        this.channelQueue.Add(channel);
    }
    public async Task Publish(string exchange, string routingKey, string message)
    {
        var channel = this.channelQueue.Take();
        var body = Encoding.UTF8.GetBytes(message);
        await channel.Publish(exchange, routingKey, body);
        this.channelQueue.Add(channel);
    }
    public void Schedule(string exchange, string routingKey, DateTime scheduleTimeUtc, string message)
    {
        var channel = this.channelQueue.Take();
        var body = Encoding.UTF8.GetBytes(message);
        channel.Schedule(exchange, routingKey, scheduleTimeUtc, body);
        this.channelQueue.Add(channel);
    }
    public async Task Shutdown()
    {
        if (this.channels != null && this.channels.Count > 0)
        {
            foreach (var channel in this.channels.Values)
                await channel.Close();
            this.channels.Clear();
        }
        this.channels = null;
        if (this.channelQueue != null && this.channelQueue.Count > 0)
            while (this.channelQueue.TryTake(out _)) ;
        this.channelQueue = null;
        if (this.connection != null)
            await this.connection.CloseAsync();
        this.connection = null;
    }
    public void Dispose() => this.Shutdown().Wait();
}
class ProducerChannel
{
    private BasicProperties properties;
    public IChannel Channel { get; private set; }
    public static async Task<ProducerChannel> Create(IConnection connection)
    {
        var channel = await connection.CreateChannelAsync();
        var properties = new BasicProperties { Persistent = true };
        return new ProducerChannel() { Channel = channel, properties = properties };
    }
    public async Task CreateExchange(string exchangeName, string bindType, bool isDelay)
    {
        Dictionary<string, object> arguments = null;
        if (isDelay) arguments = new Dictionary<string, object> { { "x-delayed-type", "topic" } };
        await this.Channel.ExchangeDeclareAsync(exchangeName, bindType, true, false, arguments);
    }
    public async Task CreateQueue(string queueName, bool isSac, bool isHeartbeat)
    {
        IDictionary<string, object> arguments = null;
        if (isSac) arguments = new Dictionary<string, object> { { "x-single-active-consumer", true } };
        if (isHeartbeat) await this.Channel.QueueDeclareAsync(queueName, false, true, false, arguments);
        else await this.Channel.QueueDeclareAsync(queueName, true, false, false, arguments);
    }
    public async Task BindQueue(string exchange, string queueName, string bindingKey)
        => await this.Channel.QueueBindAsync(queueName, exchange, bindingKey);
    public async Task Publish(string exchange, string routingKey, byte[] message)
        => await this.Channel.BasicPublishAsync(exchange, routingKey, true, this.properties, message);
    public void Schedule(string exchange, string routingKey, DateTime scheduleTimeUtc, byte[] message)
    {
        var delayMilliseconds = scheduleTimeUtc.Subtract(DateTime.UtcNow).TotalMilliseconds;
        var properties = new BasicProperties
        {
            Persistent = true,
            Headers = new Dictionary<string, object> { { "x-delay", (long)delayMilliseconds } }
        };
        this.Channel.BasicPublishAsync(exchange, routingKey, true, properties, message);
    }
    public async Task Close() => await this.Channel.CloseAsync();
}