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
    private static BasicProperties properties = new BasicProperties { Persistent = true };
    private IConnection connection;
    private ConcurrentDictionary<int, IChannel> channels;
    private BlockingCollection<IChannel> channelQueue;

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
        var channels = new ConcurrentDictionary<int, IChannel>();
        var channelQueue = new BlockingCollection<IChannel>();
        for (int i = 0; i < channelSize; i++)
        {
            var channel = await connection.CreateChannelAsync();
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
        Dictionary<string, object> arguments = null;
        if (isDelay) arguments = new Dictionary<string, object> { { "x-delayed-type", "topic" } };
        await channel.ExchangeDeclareAsync(exchangeName, bindType, true, false, arguments);
        this.channelQueue.Add(channel);
    }
    public async Task CreateQueue(string queueName, bool isSac, bool isExclusive)
    {
        var channel = this.channelQueue.Take();
        IDictionary<string, object> arguments = null;
        if (isSac) arguments = new Dictionary<string, object> { { "x-single-active-consumer", true } };
        if (isExclusive) await channel.QueueDeclareAsync(queueName, false, true, false, arguments);
        else await channel.QueueDeclareAsync(queueName, true, false, false, arguments);
        this.channelQueue.Add(channel);
    }
    public async Task BindQueue(string exchange, string queueName, string bindingKey)
    {
        var channel = this.channelQueue.Take();
        await channel.QueueBindAsync(queueName, exchange, bindingKey);
        this.channelQueue.Add(channel);
    }
    public async Task Publish(string exchange, string routingKey, string message)
    {
        var channel = this.channelQueue.Take();
        var body = Encoding.UTF8.GetBytes(message);
        await channel.BasicPublishAsync(exchange, routingKey, true, properties, body);
        this.channelQueue.Add(channel);
    }
    public async void Schedule(string exchange, string routingKey, DateTime scheduleTimeUtc, string message)
    {
        var channel = this.channelQueue.Take();
        var body = Encoding.UTF8.GetBytes(message);
        var delayMilliseconds = scheduleTimeUtc.Subtract(DateTime.UtcNow).TotalMilliseconds;
        var properties = new BasicProperties
        {
            Persistent = true,
            Headers = new Dictionary<string, object> { { "x-delay", (long)delayMilliseconds } }
        };
        await channel.BasicPublishAsync(exchange, routingKey, true, properties, body);
        this.channelQueue.Add(channel);
    }
    public async Task Shutdown()
    {
        if (this.channels != null && this.channels.Count > 0)
        {
            foreach (var channel in this.channels.Values)
                await channel.CloseAsync();
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