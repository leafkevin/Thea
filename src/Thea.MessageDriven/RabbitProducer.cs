using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RabbitMQ.Client;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Thea.MessageDriven;

class RabbitProducer : IDisposable
{
    private IConnection connection;
    private Channel<IChannel> channel;
    public string ConnectionName { get; private set; }

    private RabbitProducer(Channel<IChannel> channel, IConnection connection, string connectionName)
    {
        this.channel = channel;
        this.connection = connection;
        this.ConnectionName = connectionName;
    }

    public static async Task<RabbitProducer> CreateAsync(MessageDrivenService parent, IServiceProvider serviceProvider, int channelSize = 15)
    {
        var connectionId = $"producer.{parent.ServiceId}";
        var configuration = serviceProvider.GetService<IConfiguration>();
        var user = configuration.GetValue<string>("MessageDriven:User");
        var password = configuration.GetValue<string>("MessageDriven:Password");

        var factory = new ConnectionFactory
        {
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
        var connection = await factory.CreateConnectionAsync(parent.tcpEndPoints, connectionId);
        var myChannel = Channel.CreateBounded<IChannel>(new BoundedChannelOptions(channelSize)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleWriter = true,
            SingleReader = true
        });
        for (int i = 0; i < channelSize; i++)
        {
            var channel = await connection.CreateChannelAsync();
            await myChannel.Writer.WriteAsync(channel);
        }
        return new RabbitProducer(myChannel, connection, connectionId);
    }
    public async Task CreateExchange(string exchangeName, string bindType, bool isDelay = false)
    {
        var rabbitChannel = await this.channel.Reader.ReadAsync();
        Dictionary<string, object> arguments = null;
        if (isDelay) arguments = new Dictionary<string, object> { { "x-delayed-type", "topic" } };
        await rabbitChannel.ExchangeDeclareAsync(exchangeName, bindType, true, false, arguments);
        await this.channel.Writer.WriteAsync(rabbitChannel);
    }
    public async Task CreateQueue(string queueName, bool isQuorumQueue, bool isSingleActiveConsumer, bool isExclusive)
    {
        var rabbitChannel = await this.channel.Reader.ReadAsync();
        IDictionary<string, object> arguments = null;

        if (isExclusive) await rabbitChannel.QueueDeclareAsync(queueName, false, true, false);
        else
        {
            if (isSingleActiveConsumer || isQuorumQueue) arguments = new Dictionary<string, object>();
            {
                if (isSingleActiveConsumer) arguments.Add("x-single-active-consumer", true);
                if (isQuorumQueue) arguments.Add("x-queue-type", "quorum");
            }
            await rabbitChannel.QueueDeclareAsync(queueName, true, false, false, arguments);
        }
        await this.channel.Writer.WriteAsync(rabbitChannel);
    }
    public async Task BindExchange(string exchange, string toExchange, string routingKey)
    {
        var rabbitChannel = await this.channel.Reader.ReadAsync();
        await rabbitChannel.ExchangeBindAsync(toExchange, exchange, routingKey);
        await this.channel.Writer.WriteAsync(rabbitChannel);
    }
    public async Task BindQueue(string exchange, string queueName, string routingKey)
    {
        var rabbitChannel = await this.channel.Reader.ReadAsync();
        await rabbitChannel.QueueBindAsync(queueName, exchange, routingKey);
        await this.channel.Writer.WriteAsync(rabbitChannel);
    }
    public async Task RemoveQueue(string queueName)
    {
        var rabbitChannel = await this.channel.Reader.ReadAsync();
        try
        {
            await rabbitChannel.QueuePurgeAsync(queueName);
            await rabbitChannel.QueueDeleteAsync(queueName);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"RemoveQueue error: {ex}");
        }
        await this.channel.Writer.WriteAsync(rabbitChannel);
    }
    public async Task PublishAsync(string exchange, string routingKey, BasicProperties properties, string message)
    {
        var rabbitChannel = await this.channel.Reader.ReadAsync();
        var body = Encoding.UTF8.GetBytes(message);
        await rabbitChannel.BasicPublishAsync(exchange, routingKey, true, properties, body);
        await this.channel.Writer.WriteAsync(rabbitChannel);
    }
    public async Task Shutdown()
    {
        if (this.channel != null)
        {
            while (this.channel.Reader.TryRead(out var rabbitChannel))
                await rabbitChannel.CloseAsync();
        }
        this.channel.Writer.TryComplete();
        if (this.connection != null)
            await this.connection.CloseAsync();
        this.connection = null;
    }
    public void Dispose() => this.Shutdown().Wait();
}