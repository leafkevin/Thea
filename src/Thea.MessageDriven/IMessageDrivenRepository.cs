using System.Collections.Generic;
using System.Threading.Tasks;

namespace Thea.MessageDriven;

public interface IMessageDrivenRepository
{
    Task<List<Cluster>> GetClusters(List<string> clusterIds);
    Task Register(List<Cluster> clusters);
    Task WriteLogs(List<ExecLog> logInfos);
}
