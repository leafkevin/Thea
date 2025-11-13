using System.Collections.Generic;
using System.Threading.Tasks;

namespace Thea.MessageDriven;

public interface IMessageDrivenRepository
{
    Task<List<Setting>> GetSettings(bool useCache = true);
    Task Register(List<Setting> settings);
    Task Change(string queue, int workloadTotal, int? prefetchCount = null, bool? isLogEnabled = null);
    Task WriteLogs(List<ExecLog> logInfos);
}