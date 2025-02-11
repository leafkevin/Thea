using System;
using System.Collections.Generic;

namespace Thea.MessageDriven;

enum MessageType
{
    Message,
    Heartbeat,
    WaitForStart,
    WaitForShutdown,
    Logs
}
enum ChangeType
{
    None,
    AddQueue,
    RemoveQueue,
    BindingChanged
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

class ConsumerInfo
{
    public string ConsumerId { get; set; }
    public string Queue { get; set; }
    public bool IsRunning { get; set; }
}
class WaitForStartMessage
{
    public int WaitTotal { get; set; }
    public List<string> QueueNames { get; set; }
    public List<RabbitConsumer> Consumers { get; set; } = new();
}
