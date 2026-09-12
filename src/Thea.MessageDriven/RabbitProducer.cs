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
                { "client_api", "Thea.MessageDriven" }
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
        await this.TryTo($"CreateExchange失败，exchange: {exchangeName}，bindType：{bindType}，isDelay：{isDelay}", async channel =>
        {
            Dictionary<string, object> arguments = null;
            if (isDelay) arguments = new Dictionary<string, object> { { "x-delayed-type", "topic" } };
            await channel.ExchangeDeclareAsync(exchangeName, bindType, true, false, arguments);
        });
    }
    public async Task CreateQueue(string queueName, bool isQuorumQueue, bool isSingleActiveConsumer, bool isExclusive)
    {
        await this.TryTo($"CreateQueue失败，queueName: {queueName}，isQuorumQueue：{isQuorumQueue}，isSingleActiveConsumer：{isSingleActiveConsumer}，isExclusive：{isExclusive}", async channel =>
        {
            IDictionary<string, object> arguments = null;
            if (isExclusive) await channel.QueueDeclareAsync(queueName, false, true, false);
            else
            {
                if (isSingleActiveConsumer || isQuorumQueue) arguments = new Dictionary<string, object>();
                {
                    if (isSingleActiveConsumer) arguments.Add("x-single-active-consumer", true);
                    if (isQuorumQueue) arguments.Add("x-queue-type", "quorum");
                }
                await channel.QueueDeclareAsync(queueName, true, false, false, arguments);
            }
        });
    }
    public async Task BindExchange(string fromExchange, string toExchange, string routingKey)
    {
        await this.TryTo($"BindExchange失败，fromExchange: {fromExchange}，toExchange：{toExchange}，routingKey：{routingKey}", async channel =>
        {
            await channel.ExchangeBindAsync(toExchange, fromExchange, routingKey);
        });
    }
    public async Task BindQueue(string exchange, string queueName, string routingKey)
    {
        await this.TryTo($"BindQueue失败，exchange: {exchange}，queueName：{queueName}，routingKey：{routingKey}", async channel =>
        {
            await channel.QueueBindAsync(queueName, exchange, routingKey);
        });
    }
    public async Task RemoveQueue(string queueName)
    {
        var rabbitChannel = await this.channel.Reader.ReadAsync();
        try
        {
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
        await this.TryTo($"发送消息异常，Message: {message}", async channel =>
        {
            var body = Encoding.UTF8.GetBytes(message);
            await channel.BasicPublishAsync(exchange, routingKey, true, properties, body);
        });
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

    private async Task TryTo(string errMessage, Func<IChannel, Task> work)
    {
        var rabbitChannel = await this.channel.Reader.ReadAsync();
        try
        {
            await work.Invoke(rabbitChannel);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"{errMessage}, Exception: {ex}");
            //发生异常，就重建channel
            await rabbitChannel.DisposeAsync();
            rabbitChannel = await connection.CreateChannelAsync();
        }
        await this.channel.Writer.WriteAsync(rabbitChannel);
    }
}