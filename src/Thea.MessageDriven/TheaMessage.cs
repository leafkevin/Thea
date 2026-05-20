using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Thea.MessageDriven;

enum MessageType
{
    Message,
    RpcMessage,
    DelayedMessage,
    RpcFailure,
    Heartbeat,
    WaitStarting,
    WaitShutdowning,
    Logs
}
enum QueueType
{
    Message,
    Heartbeat,
    RpcResult,
    Transfer
}
class Message
{
    public string MessageId { get; set; }
    public string Type { get; set; }
    public string TraceId { get; set; }
    /// <summary>
    /// 心跳时是AppId
    /// </summary>
    public string From { get; set; }
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
}
class Message<TBody>
{
    public string MessageId { get; set; }
    public string From { get; set; }
    public string Type { get; set; }
    public string TraceId { get; set; }
    public string Exchange { get; set; }
    public string RoutingKey { get; set; }
    public DateTime? ScheduleTimeUtc { get; set; }
    public TBody Body { get; set; }
}
class ConsumerWaiter
{
    public int WaitTotal { get; set; }
    public List<string> QueueNames { get; set; } = new();
    public List<RabbitConsumer> Consumers { get; set; } = new();
}
class RpcWaiter
{
    public string MessageId { get; set; }
    public TaskCompletionSource<Message<string>> Waiter { get; set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int TimeoutSeconds { get; set; } = 30;
    public string TimeoutMessage { get; set; }
}