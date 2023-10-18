using System;
using System.Threading.Tasks;

namespace Thea.MessageDriven;

public class TheaMessage
{
    public string MessageId { get; set; }
    public string ReplyExchange { get; set; }
    public string ReplyRoutingKey { get; set; }
    public MessageStatus? Status { get; set; }
    public string Message { get; set; }
}
public enum MessageStatus
{
    None = 0,
    WaitForReply,
    SetResult,
    SetException,
    Completed
}
enum MessageType
{
    TheaMessage,
    OrgMessage,
    Logs
}
class Message
{
    public MessageType Type { get; set; }
    public string Exchange { get; set; }
    public string RoutingKey { get; set; }
    public DateTime? ScheduleTimeUtc { get; set; }
    public object Body { get; set; }
}
class ResultWaiter
{
    public Type ResponseType { get; set; }
    public TaskCompletionSource<object> Waiter { get; set; }
}
class ProducerInfo
{
    public string ClusterId { get; set; }
    public string Exchange { get; set; }
    public int ConsumerTotalCount { get; set; }
    public RabbitProducer RabbitProducer { get; set; }
    public bool IsNeedHashRoutingKey { get; set; }
}
class ConsumerInfo
{
    public string ClusterId { get; set; }
    public string ConsumerId { get; set; }
    public string Exchange { get; set; }
    public string BindType { get; set; }
    public string RoutingKey { get; set; }
    public string Queue { get; set; }
    public bool IsStateful { get; set; }
    public bool IsDelay { get; set; }
    public RabbitConsumer RabbitConsumer { get; set; }
}
class ConsumerExecutor
{
    public string ClusterId { get; set; }
    public Type ParameterType { get; set; }
    public Type ReturnType { get; set; }
    public ObjectMethodExecutor MethodExecutor { get; set; }
}
