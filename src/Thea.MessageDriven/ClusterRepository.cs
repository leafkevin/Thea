using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Thea.Orm;

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
    public async Task<(List<Cluster>, List<Binding>, List<Consumer>)> GetClusterInfo(string nodeId, List<string> clusterIds)
    {
        using var repository = this.dbFactory.Create(this.dbKey);
        return (await repository.QueryAsync<Cluster>(f => clusterIds.Contains(f.ClusterId)),
            await repository.QueryAsync<Binding>(f => clusterIds.Contains(f.ClusterId)),
            await repository.QueryAsync<Consumer>(f => f.HostName == nodeId && clusterIds.Contains(f.ClusterId)));
    }
    public async Task<int> Register(List<Cluster> clusters)
    {
        using var repository = this.dbFactory.Create(this.dbKey);
        return await repository.CreateAsync<Cluster>(clusters);
    }
    public async Task<int> Register(List<Binding> bindings)
    {
        using var repository = this.dbFactory.Create(this.dbKey);
        return await repository.CreateAsync<Binding>(bindings);
    }
    public async Task<int> Register(List<Consumer> consumers)
    {
        using var repository = this.dbFactory.Create(this.dbKey);
        return await repository.CreateAsync<Consumer>(consumers);
    }
    public async Task Update(string hostName)
    {
        using var repository = this.dbFactory.Create(this.dbKey);
        await repository.UpdateAsync<Consumer>(f => new { LastExecutedTime = DateTime.Now }, f => f.HostName == hostName);
        repository.Close();
    }
    public async Task AddLogs(List<ExecLog> logInfos)
    {
        using var repository = this.dbFactory.Create(this.dbKey);
        await repository.CreateAsync<ExecLog>(logInfos);
        repository.Close();
    }
}
