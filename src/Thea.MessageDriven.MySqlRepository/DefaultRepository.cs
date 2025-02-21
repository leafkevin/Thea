using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
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
    public virtual async Task<(List<Queue>, List<Binding>)> GetConfigInfo()
    {
        var cacheKey = "mds.config.all";
        return await this.redisCache.GetOrCreateAsync(cacheKey, async () =>
        {
            var repository = this.dbFactory.Create(this.dbKey);
            using var reader = await repository.QueryMultipleAsync(t =>
            {
                t.Query<Queue>(f => f.IsEnabled);
                t.Query<Binding>();
            });
            var queues = await reader.ReadAsync<Queue>();
            var bindings = await reader.ReadAsync<Binding>();
            return (queues, bindings);
        });
    }
    public virtual async Task Register(List<Queue> queues, List<Binding> bindings)
    {
        var repository = this.dbFactory.Create(this.dbKey);
        await repository.Create<Queue>()
            .IgnoreInto().WithBulk(queues)
            .ExecuteAsync();
        await repository.Create<Binding>()
            .IgnoreInto().WithBulk(bindings)
            .ExecuteAsync();
    }
    public virtual async Task WriteLogs(List<ExecLog> logInfos)
    {
        var repository = this.dbFactory.CreateRepository(this.dbKey);
        await repository.CreateAsync<ExecLog>(logInfos);
    }
}