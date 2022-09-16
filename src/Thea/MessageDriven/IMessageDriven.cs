using System;
using System.Threading.Tasks;

namespace Thea
{
    public interface IMessageDriven
    {
        void Start();
        void Shutdown();

        Task<ActionResponse> TryProcessMessage(string exchange, string routingKey, Message message);
        string TryProcessMessageAsync(string exchange, string routingKey, Message message);
        Task<ActionResponse[]> TryProcessGroupMessage(string exchange, Message message, Func<object, string> groupRoutingKeyAcquirer);
        string[] TryProcessGroupMessageAsync(string exchange, Message message, Func<object, string> groupRoutingKeyAcquirer);
    }
}
