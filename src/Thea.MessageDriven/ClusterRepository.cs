using System.Collections.Generic;
using System.Threading.Tasks;
using Trolley;

namespace Thea.MessageDriven;

class ClusterRepository
{
    private readonly string dbKey;
    private readonly IOrmDbFactory dbFactory;
    public ClusterRepository(IOrmDbFactory dbFactory, string dbKey)
    {
        this.dbFactory = dbFactory;
        this.dbKey = dbKey;
    }
    public async Task<(List<Cluster>, List<Binding>)> GetClusterInfo(List<string> clusterIds)
    {
        var repository = this.dbFactory.CreateRepository(this.dbKey);
        var clusters = await repository.QueryAsync<Cluster>(f => clusterIds.Contains(f.ClusterId));
        var bindings = await repository.QueryAsync<Binding>(f => clusterIds.Contains(f.ClusterId));
        return (clusters, bindings);
    }
    public async Task<int> Register(List<Cluster> clusters)
    {
        var repository = this.dbFactory.CreateRepository(this.dbKey);
        return await repository.CreateAsync<Cluster>(clusters);
    }
    public async Task<int> Register(List<Binding> bindings)
    {
        var repository = this.dbFactory.CreateRepository(this.dbKey);
        return await repository.CreateAsync<Binding>(bindings);
    }
    public async Task AddLogs(List<ExecLog> logInfos)
    {
        var repository = this.dbFactory.CreateRepository(this.dbKey);
        await repository.CreateAsync<ExecLog>(logInfos);
    }
}
