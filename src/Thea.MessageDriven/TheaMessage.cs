using System;
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
    public bool IsJsonMessage { get; set; }
    public object Body { get; set; }
    [JsonIgnore]
    public TaskCompletionSource<bool> Waiter { get; set; }
}
struct RpcResponse
{
    public string Type { get; set; }
    public string Body { get; set; }
}
class RpcWaiter
{
    public string MessageId { get; set; }
    public TaskCompletionSource<RpcResponse> Waiter { get; set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int TimeoutSeconds { get; set; } = 30;
    public string TimeoutMessage { get; set; }
}