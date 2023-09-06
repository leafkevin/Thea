using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Thea.MessageDriven;

class TheaMessage
{
    public string MessageId { get; set; }
    public string NodeId { get; set; }
    public string ClusterId { get; set; }
    public string Exchange { get; set; }
    public string ReplyExchange { get; set; }
    public string RoutingKey { get; set; }
    public string Queue { get; set; }
    public bool IsGroupMessage { get; set; }
    public MessageStatus Status { get; set; }

    public string Message { get; set; }
    [JsonIgnore]
    public TaskCompletionSource<TheaResponse> Waiter { get; set; }
}
