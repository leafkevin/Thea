using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Thea.Logging;

namespace Thea.MessageDriven;

class RabbitProducer
{
    //private Task task;
    private readonly MessageDrivenService parent;
    private readonly ConnectionFactory factory;
    private IConnection connection;
    //private Channel<Message> messageQueue;
    private CancellationTokenSource stoppingToken = new();
    private readonly BlockingCollection<IChannel> channelQueue;
    private readonly ILogger<RabbitProducer> logger;
    //private volatile Dictionary<string, Binding> statefulBindings;

    //0:正在运行，1:正在关闭中，2:已完成关闭
    private int state;
    private int channelSize;
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
        //this.messageQueue = Channel.CreateBounded<Message>(new BoundedChannelOptions(5000)
        //{
        //    FullMode = BoundedChannelFullMode.Wait,
        //    SingleWriter = false,
        //    SingleReader = true
        //});
        this.channelQueue = new(channelSize);
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
                this.channelQueue.Add(channel);
            }
        }
        catch
        {
            while (this.channelQueue.TryTake(out var rabbitChannel))
                await rabbitChannel.DisposeAsync();
            await this.connection.DisposeAsync();
            throw;
        }
    }
    //public async Task StartLoopAsync()
    //{
    //    this.task = Task.Run(async () =>
    //    {
    //        while (!this.stoppingToken.IsCancellationRequested || this.messageQueue.Reader.TryPeek(out _))
    //        {
    //            Message message = null;
    //            BasicProperties properties = null;
    //            try
    //            {
    //                for (int i = 0; i < 10; i++)
    //                {
    //                    if (!this.messageQueue.Reader.TryRead(out message))
    //                        break;
    //                    try
    //                    {
    //                        switch (message.Type)
    //                        {
    //                            case Consts.UserMessage:
    //                            case Consts.RpcMessage:
    //                                properties = new BasicProperties
    //                                {
    //                                    Persistent = true,
    //                                    Type = message.Type,
    //                                    DeliveryMode = DeliveryModes.Persistent,
    //                                    AppId = this.parent.AppId,
    //                                    MessageId = message.MessageId,
    //                                    Headers = new Dictionary<string, object> { { "TraceId", message.TraceId } }
    //                                };
    //                                if (message.ScheduleTimeUtc.HasValue)
    //                                {
    //                                    var delayMilliseconds = (long)message.ScheduleTimeUtc.Value.Subtract(DateTime.UtcNow).TotalMilliseconds;
    //                                    properties.Headers.Add("x-delay", delayMilliseconds);
    //                                }
    //                                if (message.Type == Consts.RpcMessage)
    //                                {
    //                                    //RPC消息，设置回复队列和过期时间
    //                                    properties.ReplyTo = message.ReplyTo;
    //                                    properties.CorrelationId = message.MessageId;
    //                                }
    //                                var jsonMessage = message.Body.ToString();
    //                                if (this.statefulBindings.TryGetValue(message.Exchange, out var myBinding))
    //                                {
    //                                    if (myBinding.AppId == this.parent.AppId)
    //                                    {
    //                                        //如果存在有状态队列，根据消息的RoutingKey进行一致性哈希，路由到对应的队列中
    //                                        var routingKey = JumpConsistentHash.GetBucket(message.RoutingKey, myBinding.WorkloadTotal);
    //                                        await this.PublishAsync(message.Exchange, routingKey.ToString(), properties, jsonMessage);
    //                                    }
    //                                    else
    //                                    {
    //                                        //如果队列消费者是其他应用的，发到转发队列中
    //                                        properties.Headers.Add("Exchange", message.Exchange);
    //                                        properties.Headers.Add("RoutingKey", message.RoutingKey);
    //                                        var transferQueue = $"{Consts.TransferExchange}.{myBinding.AppId}";
    //                                        await this.PublishAsync(Consts.DefaultExchange, transferQueue, properties, jsonMessage);
    //                                    }
    //                                }
    //                                //如果是无状态队列的消息，直接发送交换机
    //                                else await this.PublishAsync(message.Exchange, message.RoutingKey, properties, jsonMessage);
    //                                message.Waiter?.TrySetResult(true);
    //                                break;
    //                            case Consts.SendShutdownMessages:
    //                                //TODO: 发送关闭消息给消费者

    //                                break;
    //                            case Consts.ChangeConsumers:
    //                                this.statefulBindings = message.Body as Dictionary<string, Binding>;
    //                                break;
    //                            case Consts.Shutdown:
    //                                Interlocked.Exchange(ref this.state, 2);
    //                                message.Waiter.TrySetResult(true);
    //                                break;
    //                        }
    //                    }
    //                    catch (Exception ex)
    //                    {
    //                        if (message.Type == Consts.UserMessage || message.Type == Consts.RpcMessage)
    //                        {
    //                            message.RetryTimes++;
    //                            if (message.RetryTimes > 3)
    //                            {
    //                                var exception = ex.InnerException ?? ex;
    //                                var errMessage = $"发送消息失败，RetryTimes: {message.RetryTimes - 1}，Message: {message.ToJson()}";
    //                                this.logger.LogTagError("MessageDriven", exception, errMessage);
    //                                message.Waiter?.TrySetException(new Exception(errMessage, exception));
    //                            }
    //                            else if (!this.messageQueue.Writer.TryWrite(message))
    //                            {
    //                                var retryException = new InvalidOperationException("消息发送失败且内部Channel已关闭，无法重新入队。", ex);
    //                                message.Waiter?.TrySetException(retryException);
    //                                this.logger.LogTagError("MessageDriven", retryException, $"Message: {message.ToJson()}");
    //                            }
    //                        }
    //                    }
    //                }
    //                if (!this.messageQueue.Reader.TryPeek(out _))
    //                    await Task.Delay(1);
    //            }
    //            catch (Exception ex)
    //            {
    //                var exception = ex.InnerException ?? ex;
    //                this.logger.LogTagError("MessageDriven", exception, $"ExecuteAsync is failled");
    //            }
    //        }
    //    }, this.stoppingToken.Token);
    //}
    public async Task CreateExchange(string exchangeName, string bindType, bool isDelay = false)
    {
        await this.TryTo($"CreateExchange失败，exchange: {exchangeName}，bindType：{bindType}，isDelay：{isDelay}", async (channel, cancellationToken) =>
        {
            Dictionary<string, object> arguments = null;
            if (isDelay) arguments = new Dictionary<string, object> { { "x-delayed-type", "topic" } };
            await channel.ExchangeDeclareAsync(exchangeName, bindType, true, false, arguments, cancellationToken: cancellationToken);
        });
    }
    public async Task CreateQueue(string queueName, bool isQuorumQueue, bool isSingleActiveConsumer, bool isExclusive)
    {
        await this.TryTo($"CreateQueue失败，queueName: {queueName}，isQuorumQueue：{isQuorumQueue}，isSingleActiveConsumer：{isSingleActiveConsumer}，isExclusive：{isExclusive}", async (channel, cancellationToken) =>
        {
            IDictionary<string, object> arguments = null;
            if (isExclusive) await channel.QueueDeclareAsync(queueName, false, true, false, cancellationToken: cancellationToken);
            else
            {
                if (isSingleActiveConsumer || isQuorumQueue) arguments = new Dictionary<string, object>();
                {
                    if (isSingleActiveConsumer) arguments.Add("x-single-active-consumer", true);
                    if (isQuorumQueue) arguments.Add("x-queue-type", "quorum");
                }
                await channel.QueueDeclareAsync(queueName, true, false, false, arguments, cancellationToken: cancellationToken);
            }
        });
    }
    public async Task BindExchange(string fromExchange, string toExchange, string routingKey)
    {
        await this.TryTo($"BindExchange失败，fromExchange: {fromExchange}，toExchange：{toExchange}，routingKey：{routingKey}", async (channel, cancellationToken) =>
        {
            await channel.ExchangeBindAsync(toExchange, fromExchange, routingKey, cancellationToken: cancellationToken);
        });
    }
    public async Task BindQueue(string exchange, string queueName, string routingKey)
    {
        await this.TryTo($"BindQueue失败，exchange: {exchange}，queueName：{queueName}，routingKey：{routingKey}", async (channel, cancellationToken) =>
        {
            await channel.QueueBindAsync(queueName, exchange, routingKey, cancellationToken: cancellationToken);
        });
    }
    public async Task RemoveQueue(string queueName)
    {
        await this.TryTo($"RemoveQueue失败，queueName: {queueName}", async (channel, cancellationToken) =>
        {
            await channel.QueueDeleteAsync(queueName, cancellationToken: cancellationToken);
        });
    }
    //public async Task TryPublishAsync(Message message)
    //    => await this.messageQueue.Writer.WriteAsync(message);
    public async Task PublishAsync(string exchange, string routingKey, BasicProperties properties, string message)
    {
        await this.TryTo($"发送消息异常，Message: {message}", async (channel, cancellationToken) =>
        {
            var body = Encoding.UTF8.GetBytes(message);
            await channel.BasicPublishAsync(exchange, routingKey, true, properties, body, cancellationToken);
        });
    }
    public async Task ShutdownAsync()
    {
        if (Interlocked.Exchange(ref this.state, 1) >= 1)
            return;
        try
        {
            //发送关闭生产者标志
            //var shutdownMessage = new Message
            //{
            //    Type = Consts.Shutdown,
            //    Waiter = new(TaskCreationOptions.RunContinuationsAsynchronously)
            //};
            //await this.messageQueue.Writer.WriteAsync(shutdownMessage);
            //await shutdownMessage.Waiter.Task;
            //this.stoppingToken.Cancel();
            //this.messageQueue.Writer.TryComplete();
            while (this.channelQueue.TryTake(out var rabbitChannel))
                await rabbitChannel.DisposeAsync();
            if (this.connection != null)
                await this.connection.DisposeAsync();
            this.connection = null;
            this.stoppingToken.Dispose();
            this.channelQueue.Dispose(); 
        }
        catch (Exception ex)
        {
            this.logger.LogTagError("RabbitProducer", ex, $"ShutdownAsync，关闭RabbitMQ生产者失败, ConnectionId: {this.ConnectionId}");
            Console.WriteLine($"ShutdownAsync，关闭RabbitMQ生产者失败, ConnectionId: {this.ConnectionId}, Exception: {ex}");
        }
    }
    private async Task TryTo(string errMessage, Func<IChannel, CancellationToken, Task> work)
    {
        if (this.stoppingToken == null || this.stoppingToken.IsCancellationRequested)
            return;
        if (Volatile.Read(ref this.state) == 2)
            return;

        IChannel rabbitChannel = null;
        try
        {
            rabbitChannel = this.channelQueue.Take();
            await work.Invoke(rabbitChannel, this.stoppingToken.Token);
        }
        catch (Exception ex)
        {
            this.logger.LogTagError("RabbitProducer", ex, $"TryTo，{errMessage}, ConnectionId: {this.ConnectionId}");
            Console.WriteLine($"{errMessage}, ConnectionId: {this.ConnectionId}, Exception: {ex}");
            try
            {
                await rabbitChannel.DisposeAsync();
                rabbitChannel = await this.connection.CreateChannelAsync();
            }
            catch (Exception myEx)
            {
                this.logger.LogTagError("RabbitProducer", myEx, $"TryTo，重建Channel失败，{errMessage}, ConnectionId: {this.ConnectionId}");
                Console.WriteLine($"TryTo，重建Channel失败，{errMessage}, ConnectionId: {this.ConnectionId}, Exception: {myEx}");
            }
        }
        finally
        {
            this.channelQueue.Add(rabbitChannel);
        }
    }
}