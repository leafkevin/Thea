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
    public async Task<List<Cluster>> GetClusters(List<string> clusterIds)
    {
        using var repository = this.dbFactory.Create(this.dbKey);
        return await repository.QueryAsync<Cluster>(f => clusterIds.Contains(f.ClusterId));
    }
    public async Task<List<Binding>> GetBindings(List<string> clusterIds)
    {
        using var repository = this.dbFactory.Create(this.dbKey);
        return await repository.QueryAsync<Binding>(f => clusterIds.Contains(f.ClusterId));
    }
    public async Task<List<Consumer>> GetConsumers(string nodeId)
    {
        using var repository = this.dbFactory.Create(this.dbKey);
        return await repository.QueryAsync<Consumer>(f => f.HostName == nodeId);
    }
    public async Task Update(string nodeId)
    {
        using var repository = this.dbFactory.Create(this.dbKey);
        await repository.UpdateAsync<Consumer>(f => new { LastExecutedTime = DateTime.Now }, f => f.HostName == nodeId);
        repository.Close();
    }
    public async Task AddLog(ExecLog logInfo)
    {
        using var repository = this.dbFactory.Create(this.dbKey);
        await repository.CreateAsync<ExecLog>(logInfo);
        repository.Close();
    }
}
