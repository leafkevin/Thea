using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Trolley;
using Trolley.MySqlConnector;

namespace Thea.MessageDriven;

public class DefaultRepository : IMessageDrivenRepository
{
    private readonly string dbKey;
    private readonly IOrmDbFactory dbFactory;
    private readonly IDistributedCache redisCache;
    public DefaultRepository(IServiceProvider serviceProvider)
    {
        this.dbFactory = serviceProvider.GetService<IOrmDbFactory>();
        var configuration = serviceProvider.GetService<IConfiguration>();
        this.dbKey = configuration.GetValue<string>("MessageDriven:DbKey");
        this.redisCache = serviceProvider.GetService<IDistributedCache>();
    }
    public virtual async Task<List<Cluster>> GetClusters(List<string> clusterIds)
    {
        var result = new List<Cluster>();
        foreach (var clusterId in clusterIds)
        {
            var cacheKey = $"mds.cluster.{clusterId}";
            var myCluster = await this.redisCache.GetOrCreateAsync(cacheKey, async () =>
            {
                var repository = this.dbFactory.Create(this.dbKey);
                return await repository.QueryFirstAsync<Cluster>(f => clusterIds.Contains(f.ClusterId));
            });
            result.Add(myCluster);
        }
        return result;
    }
    public virtual async Task Register(List<Cluster> clusters)
    {
        var repository = this.dbFactory.CreateRepository(this.dbKey);
        await repository.CreateAsync<Cluster>(clusters);
        foreach (var cluster in clusters)
        {
            var cacheKey = $"mds.cluster.{cluster.ClusterId}";
            await this.redisCache.SetAsync(cacheKey, cluster);
        }
    }
    public virtual async Task WriteLogs(List<ExecLog> logInfos)
    {
        var repository = this.dbFactory.CreateRepository(this.dbKey);
        await repository.CreateAsync<ExecLog>(logInfos);
    }
}