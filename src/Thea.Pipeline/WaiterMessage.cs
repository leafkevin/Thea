using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Thea.Pipeline;

public class WaiterMessage
{
    public int MessageType { get; set; }
    /// <summary>
    /// 有状态消息，需要路由
    /// </summary>
    public string RoutingKey { get; set; }
    [JsonIgnore]
    public object Target { get; set; }
    public object[] Parameters { get; set; }
    [JsonIgnore]
    internal TaskCompletionSource<TheaResponse> Waiter { get; set; }
}