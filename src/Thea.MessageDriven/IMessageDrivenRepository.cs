using System.Collections.Generic;
using System.Threading.Tasks;

namespace Thea.MessageDriven;

public interface IMessageDrivenRepository
{
    Task<(List<Queue>, List<Binding>)> GetConfigInfo();
    Task<bool> Register(List<Queue> queues, List<Binding> bindings);
    Task UpdateCache();
    Task WriteLogs(List<ExecLog> logInfos);
}