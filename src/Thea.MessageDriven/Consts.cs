namespace Thea.MessageDriven;

class Consts
{
    public const string DefaultExchange = "";
    public const string RpcExchange = "rpc";
    public const string HeartbeatExchange = "heartbeat";
    public const string FanoutRoutingKey = "#";
    public const string DelayBindingType = "x-delayed-message";
    public const string TopicBindingType = "topic";
}
