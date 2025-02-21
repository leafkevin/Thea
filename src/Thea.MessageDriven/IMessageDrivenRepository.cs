using System.Collections.Generic;
using System.Threading.Tasks;

namespace Thea.MessageDriven;

public interface IMessageDrivenRepository
{
    Task<List<Queue>> GetQueues(List<string> queueIds, List<string> exchangeIds);
    Task<List<Exchange>> GetExchanges(List<string> exchangeIds);
    Task Register(List<Queue> queues, List<Exchange> exchanges);
    Task WriteLogs(List<ExecLog> logInfos);
}
