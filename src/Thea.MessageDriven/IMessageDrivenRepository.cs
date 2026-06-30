using System.Collections.Generic;
using System.Threading.Tasks;

namespace Thea.MessageDriven;

public interface IMessageDrivenRepository
{
    Task<(List<Queue>, List<Binding>)> GetSettings(bool useCache = true);
    Task Register(List<Queue> queues, List<Binding> bindings);
    Task Change(Queue myQueue);
    Task WriteLogs(List<ExecLog> logInfos);
}