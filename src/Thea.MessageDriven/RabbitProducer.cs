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

class RabbitProducer
{
    private readonly MessageDrivenService parent;
    private readonly ConnectionFactory factory;
    private IConnection connection;
    private readonly Channel<IChannel> channelQueue;
    private readonly ILogger<RabbitProducer> logger;
    private readonly SemaphoreSlim lifecycleSemaphore = new(1, 1);
    private readonly TaskCompletionSource<bool> recycleWaiter = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private int channelSize;
    //0:运行中, 1:关闭中, 2:已关闭
    private int state;
    private int workers;
    public string ConnectionId { get; private set; }

    public RabbitProducer(MessageDrivenService parent, IServiceProvider serviceProvider, int channelSize = 20)
    {
        this.parent = parent;
        this.ConnectionId = $"producer.{parent.AppId}.{parent.ServiceId}";
        this.channelSize = channelSize;
        var configuration = serviceProvider.GetService<IConfiguration>();
        var user = configuration.GetValue<string>("MessageDriven:User");
        var password = configuration.GetValue<string>("MessageDriven:Password");
        this.factory = new ConnectionFactory
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
                { "connection_name",  this.ConnectionId },
                { "client_api", "Thea.MessageDriven" }
            }
        };
        this.channelQueue = Channel.CreateBounded<IChannel>(channelSize);
        this.logger = serviceProvider.GetService<ILogger<RabbitProducer>>();
    }
    public async Task CreateProducer()
    {
        this.connection = await this.factory.CreateConnectionAsync(this.parent.tcpEndPoints, this.ConnectionId);
        try
        {
            for (int i = 0; i < this.channelSize; i++)
            {
                var channel = await this.connection.CreateChannelAsync();
                await this.channelQueue.Writer.WriteAsync(channel);
            }
        }
        catch
        {
            while (this.channelQueue.Reader.TryRead(out var rabbitChannel))
                await rabbitChannel.DisposeAsync();
            await this.connection.DisposeAsync();
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
    public async Task<uint> MessageCount(string queueName)
    {
        uint result = 0;
        await this.TryTo($"MessageCount失败，queueName: {queueName}", async channel =>
            result = await channel.MessageCountAsync(queueName));
        return result;
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
        Task waiterTask;
        await this.lifecycleSemaphore.WaitAsync();
        try
        {
            if (Interlocked.CompareExchange(ref this.state, 1, 0) > 0)
                return;
            waiterTask = Volatile.Read(ref this.workers) == 0 ?
                Task.CompletedTask : this.recycleWaiter.Task;
        }
        finally
        {
            this.lifecycleSemaphore.Release();
        }

        try
        {
            await waiterTask;
            while (this.channelQueue.Reader.TryRead(out var rabbitChannel))
                await rabbitChannel.DisposeAsync();
            if (this.connection != null)
                await this.connection.DisposeAsync();
            this.connection = null;
        }
        catch (Exception ex)
        {
            this.logger.LogTagError("RabbitProducer", ex, $"ShutdownAsync，关闭RabbitMQ生产者失败, ConnectionId: {this.ConnectionId}");
            Console.WriteLine($"ShutdownAsync，关闭RabbitMQ生产者失败, ConnectionId: {this.ConnectionId}, Exception: {ex}");
        }
        finally
        {
            Interlocked.Exchange(ref this.state, 2);
        }
    }
    private async Task TryTo(string errMessage, Func<IChannel, Task> work)
    {
        await this.EnterAsync();
        IChannel rabbitChannel = null;
        try
        {
            using var timeoutSource = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try
            {
                rabbitChannel = await this.channelQueue.Reader.ReadAsync(timeoutSource.Token);
            }
            catch (OperationCanceledException ex) when (timeoutSource.IsCancellationRequested)
            {
                throw new TimeoutException($"获取RabbitMQ生产Channel超时，ConnectionId: {this.ConnectionId}, Timeout: 30s", ex);
            }
            await work.Invoke(rabbitChannel);
        }
        catch (Exception ex)
        {
            this.logger.LogTagError("RabbitProducer", ex, $"TryTo，{errMessage}, ConnectionId: {this.ConnectionId}");
            Console.WriteLine($"{errMessage}, ConnectionId: {this.ConnectionId}, Exception: {ex}");
            try
            {
                if (rabbitChannel != null)
                    await rabbitChannel.DisposeAsync();
                rabbitChannel = null;
                //关闭流程会等待所有已准入操作完成，期间仍需补回Channel，
                //否则已经准入但正在等待Channel的操作可能永久阻塞。
                if (Volatile.Read(ref this.state) < 2 && this.connection?.IsOpen == true)
                    rabbitChannel = await this.connection.CreateChannelAsync();
            }
            catch (Exception rebuildException)
            {
                this.logger.LogTagError("RabbitProducer", rebuildException, $"TryTo，重建Channel失败，{errMessage}, ConnectionId: {this.ConnectionId}");
                Console.WriteLine($"TryTo，重建Channel失败，{errMessage}, ConnectionId: {this.ConnectionId}, Exception: {rebuildException}");
            }
            throw;
        }
        finally
        {
            try
            {
                if (rabbitChannel != null)
                {
                    if (Volatile.Read(ref this.state) < 2 && rabbitChannel.IsOpen)
                        await this.channelQueue.Writer.WriteAsync(rabbitChannel);
                    else await rabbitChannel.DisposeAsync();
                }
            }
            finally
            {
                await this.ExitAsync();
            }
        }
    }
    private async Task EnterAsync()
    {
        await this.lifecycleSemaphore.WaitAsync();
        try
        {
            if (Volatile.Read(ref this.state) > 1)
                throw new Exception("RabbitProducer已关闭，无法发送消息");
            Interlocked.Increment(ref this.workers);
        }
        finally { this.lifecycleSemaphore.Release(); }
    }
    private async Task ExitAsync()
    {
        await this.lifecycleSemaphore.WaitAsync();
        try
        {
            var currentWorkers = Interlocked.Decrement(ref this.workers);
            if (this.state == 1 && currentWorkers == 0)
                this.recycleWaiter.TrySetResult(true);
        }
        finally
        {
            this.lifecycleSemaphore.Release();
        }
    }
}