using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Thea.Logging;

namespace Thea.MessageDriven;

class RabbitProducer : IDisposable
{
    private readonly ILogger<RabbitProducer> logger;
    private readonly SemaphoreSlim shutdownLock = new(1, 1);
    private readonly CancellationTokenSource shutdownSource = new();
    private readonly SemaphoreWaiter semaphoreWaiter = new();
    private IConnection connection;
    private readonly Channel<IChannel> channel;
    private int isShutdown;
    public string ConnectionName { get; private set; }

    private RabbitProducer(Channel<IChannel> channel, IConnection connection, string connectionName, ILogger<RabbitProducer> logger)
    {
        this.channel = channel;
        this.connection = connection;
        this.ConnectionName = connectionName;
        this.logger = logger;
    }

    public static async Task<RabbitProducer> CreateAsync(MessageDrivenService parent, IServiceProvider serviceProvider, int channelSize = 15)
    {
        var connectionId = $"producer.{parent.ServiceId}";
        var configuration = serviceProvider.GetService<IConfiguration>();
        var logger = serviceProvider.GetService<ILogger<RabbitProducer>>();
        var user = configuration.GetValue<string>("MessageDriven:User");
        var password = configuration.GetValue<string>("MessageDriven:Password");

        var factory = new ConnectionFactory
        {
            UserName = user,
            Password = password,
            AutomaticRecoveryEnabled = true,
            RequestedHeartbeat = TimeSpan.FromSeconds(10),
            NetworkRecoveryInterval = TimeSpan.FromSeconds(5),
            //当队列很多，消息也很多时，quorum queue选举时间会变长，默认：20s
            ContinuationTimeout = TimeSpan.FromSeconds(120),
            ClientProperties = new Dictionary<string, object>()
            {
                { "connection_name", connectionId },
                { "client_api", "Thea.MessageDriven" }
            }
        };
        var connection = await factory.CreateConnectionAsync(parent.tcpEndPoints, connectionId);
        var myChannel = Channel.CreateBounded<IChannel>(new BoundedChannelOptions(channelSize)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleWriter = false,
            SingleReader = false
        });
        try
        {
            for (int i = 0; i < channelSize; i++)
            {
                var channel = await connection.CreateChannelAsync();
                await myChannel.Writer.WriteAsync(channel);
            }
            return new RabbitProducer(myChannel, connection, connectionId, logger);
        }
        catch
        {
            while (myChannel.Reader.TryRead(out var rabbitChannel))
                await rabbitChannel.DisposeAsync();
            await connection.DisposeAsync();
            throw;
        }
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
        await this.TryTo($"RemoveQueue失败，queueName: {queueName}", async channel =>
        {
            await channel.QueueDeleteAsync(queueName);
        });
    }
    public async Task PublishAsync(string exchange, string routingKey, BasicProperties properties, string message)
    {
        await this.TryTo($"发送消息异常，Message: {message}", async channel =>
        {
            var body = Encoding.UTF8.GetBytes(message);
            await channel.BasicPublishAsync(exchange, routingKey, true, properties, body);
        });
    }
    public async Task ShutdownAsync()
    {
        await this.shutdownLock.WaitAsync();
        try
        {
            if (this.semaphoreWaiter.Synchronize(() =>
                Interlocked.CompareExchange(ref this.isShutdown, 1, 0) == 1))
                return;
            this.shutdownSource.Cancel();
            await this.semaphoreWaiter.WaitAsync(TimeSpan.FromSeconds(30));

            this.channel.Writer.TryComplete();
            while (this.channel.Reader.TryRead(out var rabbitChannel))
                await this.DisposeChannel(rabbitChannel);
            if (this.connection != null)
                await this.connection.DisposeAsync();
            this.connection = null;
            this.shutdownSource.Dispose();
        }
        catch (Exception ex)
        {
            this.logger.LogTagError("RabbitProducer", ex, $"关闭RabbitMQ生产者失败, ConnectionName: {this.ConnectionName}");
        }
        finally
        {
            this.shutdownLock.Release();
        }
    }
    public void Dispose() => this.ShutdownAsync().GetAwaiter().GetResult();
    private async Task TryTo(string errMessage, Func<IChannel, Task> work)
    {
        if (!this.semaphoreWaiter.TryEnter(() => Volatile.Read(ref this.isShutdown) == 0))
            throw new ObjectDisposedException(nameof(RabbitProducer));

        IChannel rabbitChannel = null;
        try
        {
            rabbitChannel = await this.channel.Reader.ReadAsync(this.shutdownSource.Token);
            await work.Invoke(rabbitChannel);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"{errMessage}, Exception: {ex}");
            var failedChannel = rabbitChannel;
            rabbitChannel = null;
            await this.DisposeChannel(failedChannel);
            var currentConnection = this.connection;
            if (Volatile.Read(ref this.isShutdown) == 0 && (currentConnection?.IsOpen ?? false))
            {
                try
                {
                    rabbitChannel = await currentConnection.CreateChannelAsync();
                }
                catch (Exception rebuildException)
                {
                    Console.WriteLine($"重建RabbitMQ Channel失败, ConnectionName: {this.ConnectionName}, Exception: {rebuildException}");
                }
            }
            throw;
        }
        finally
        {
            try
            {
                if (rabbitChannel != null)
                {
                    if (Volatile.Read(ref this.isShutdown) == 0 && rabbitChannel.IsOpen
                        && this.channel.Writer.TryWrite(rabbitChannel))
                        rabbitChannel = null;
                    if (rabbitChannel != null)
                        await this.DisposeChannel(rabbitChannel);
                }
            }
            finally
            {
                this.semaphoreWaiter.Exit();
            }
        }
    }
    private async Task DisposeChannel(IChannel rabbitChannel)
    {
        try
        {
            if (rabbitChannel == null) return;
            await rabbitChannel.DisposeAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"CloseChannel, ConnectionName: {this.ConnectionName}, Exception: {ex}");
        }
    }
}