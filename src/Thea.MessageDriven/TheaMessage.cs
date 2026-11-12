using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Thea.MessageDriven;


enum QueueType
{
    Message,
    Heartbeat,
    RpcResult,
    Transfer
}
public enum ChangeType
{
    None,
    ScalingUp,
    ScalingDown
}
class QueueTransfer
{
    public string FromQueue { get; set; }
    public bool IsSingleActiveConsumer { get; set; }
    public List<RabbitConsumer> Consumers { get; set; }
}
class DispatchInfo
{
    public QueueType QueueType { get; set; }
    public Queue Queue { get; set; }
    public int QueueIndex { get; set; }
    public string QueueName { get; set; }
    public int ConsumerCount { get; set; }
}
class Message
{
    public string MessageId { get; set; }
    public string Type { get; set; }
    public string TraceId { get; set; }
    /// <summary>
    /// RpcMessage响应发送的队列
    /// </summary>
    public string ReplyTo { get; set; }
    public string Exchange { get; set; }
    public string RoutingKey { get; set; }
    public DateTime? ScheduleTimeUtc { get; set; }
    public object Body { get; set; }
    [JsonIgnore]
    public TaskCompletionSource<bool> Waiter { get; set; }
    [JsonIgnore]
    public int RetryTimes { get; set; }
}
class RpcWaiter
{
    public string MessageId { get; set; }
    public TaskCompletionSource<Message> Waiter { get; set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int TimeoutSeconds { get; set; } = 30;
    public string TimeoutMessage { get; set; }
}
class TimedConsumers
{
    public string QueueId { get; set; }
    public List<RabbitConsumer> Consumers { get; set; } = new();
    public bool HasMessages { get; set; }
    public int ConfirmTimes { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public TimedConsumers(string queueId) => this.QueueId = queueId;
}
class ProducerRouting
{
    public string ExchangeId { get; set; }
    public bool IsStateful { get; set; }
    public int WorkloadTotal { get; set; }
}
class RabbitQueueInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; }
    [JsonPropertyName("vhost")]
    public string VirtualHost { get; set; }
    [JsonPropertyName("messages_ready")]
    public long? MessagesReady { get; set; }
    [JsonPropertyName("messages_unacknowledged")]
    public long? MessagesUnacknowledged { get; set; }
    [JsonPropertyName("messages")]
    public long Messages { get; set; }
    [JsonPropertyName("consumers")]
    public int Consumers { get; set; }
}