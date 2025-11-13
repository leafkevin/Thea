using System.Collections.Generic;
using System.Threading.Tasks;

namespace Thea.MessageDriven;

public interface IMessageDrivenRepository
{
    Task<List<Setting>> GetSettings(bool useCache = true);
    Task Create(List<Setting> settings);
    Task Update(List<Setting> settings);
    Task Change(string queue, int workloadTotal, int prefetchCount, bool isLogEnabled);
    Task WriteLogs(List<ExecLog> logInfos);
}