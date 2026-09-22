namespace Thea.MessageDriven;

class Consts
{
    public const string DefaultExchange = "";
    public const string RpcExchange = "rpc";
    public const string HeartbeatExchange = "heartbeat";
    public const string TransferExchange = "transfer";

    public const string TopicBindingType = "topic";
    public const string DelayBindingType = "x-delayed-message";

    public const string FanoutRoutingKey = "#";

    //消息类型
    public const string UserMessage = "UserMessage";
    public const string RpcMessage = "RpcMessage";
    public const string RpcResponse = "RpcResponse";
    public const string RpcFailure = "RpcFailure";
    public const string Heartbeat = "Heartbeat";
    public const string Logs = "Logs";
}