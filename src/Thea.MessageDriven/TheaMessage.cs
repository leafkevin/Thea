using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Thea.MessageDriven;

enum MessageType
{
    Message,
    RpcMessage,
    RpcResponse,
    RpcFailure,
    Heartbeat,
    WaitForStart,
    WaitForShutdown,
    Logs
}
enum ChangeType
{
    None,
    AddQueue,
    RemoveQueue
}
class Message
{
    public string MessageId { get; set; }
    public string AppId { get; set; }
    public MessageType Type { get; set; }
    public string Exchange { get; set; }
    public string RoutingKey { get; set; }
    public DateTime? ScheduleTimeUtc { get; set; }
    public object Body { get; set; }
}
class Message<TBody> : Message
{
    public new TBody Body { get; set; }
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
    public TaskCompletionSource<Message<string>> Waiter { get; set; } = new();
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
