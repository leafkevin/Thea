using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Thea.MessageDriven;

public interface IMessageDriven
{
    string ServiceId { get; }
    void Start();
    void Shutdown();

    Task ChangeQueue(string queueId, int workloadTotal);
    Task RemoveQueue(string queueId, int minIndex, int maxIndex);

    void Publish<TMessage>(string exchange, string routingKey, TMessage message);
    Task PublishAsync<TMessage>(string exchange, string routingKey, TMessage message);
    void PublishRpc<TMessage>(string serviceId, string messageId, string exchange, string routingKey, TMessage message);
    Task PublishRpcAsync<TMessage, TResponse>(string serviceId, string messageId, string exchange, string routingKey, TMessage message);
    TResponse Request<TMessage, TResponse>(string exchange, string routingKey, TMessage message);
    Task<TResponse> RequestAsync<TMessage, TResponse>(string exchange, string routingKey, TMessage message);
    void Schedule<TMessage>(string exchange, string routingKey, TMessage message, DateTime enqueueTimeUtc);
    Task ScheduleAsync<TMessage>(string exchange, string routingKey, TMessage message, DateTime enqueueTimeUtc);
}

public class MonitoringData
{
    public string ServiceId { get; set; }
    public string Producer { get; set; }
    public string RpcConsumer { get; set; }
    public string HeartbeatConsumer { get; set; }
    public List<ConsumerInfo> Consumers { get; set; }
    public List<WaitingConsumerInfo> WaitStartingConsumers { get; set; }
    public List<WaitingConsumerInfo> WaitShutdownConsumers { get; set; }
    public List<RpcWaiterState> RpcWaiters { get; set; }
}
public class ConsumerInfo
{
    public string QueueId { get; set; }
    public bool IsStateful { get; set; }
    public int WorkloadTotal { get; set; }
    public List<ConsumerState> States { get; set; }
}
public class WaitingConsumerInfo
{
    public string QueueId { get; set; }
    public List<string> QueueNames { get; set; } = new();
}
public class ConsumerState
{
    public string ConsumerId { get; set; }
    public string QueueName { get; set; }
    public bool IsActivated { get; set; }
    public bool IsRunning { get; set; }
}
public class RpcWaiterState
{
    public string MessageId { get; set; }
    public DateTime CreatedAt { get; set; }
}