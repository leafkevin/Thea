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
    private readonly string appId;
    private readonly IOrmDbFactory dbFactory;
    private readonly IDistributedCache redisCache;
    public DefaultRepository(IServiceProvider serviceProvider)
    {
        this.dbFactory = serviceProvider.GetService<IOrmDbFactory>();
        var configuration = serviceProvider.GetService<IConfiguration>();
        this.dbKey = configuration.GetValue<string>("MessageDriven:DbKey");
        this.appId = configuration.GetValue<string>("AppId");
        this.redisCache = serviceProvider.GetService<IDistributedCache>();
    }
    public virtual async Task<(List<Queue>, List<Binding>)> GetConfigInfo()
    {
        var cacheKey = $"{this.appId}.queue.all";
        var queues = await this.redisCache.GetOrCreateAsync(cacheKey, async () =>
        {
            var repository = this.dbFactory.Create(this.dbKey);
            var result = await repository.QueryAsync<Queue>(f => f.IsEnabled);
            if (result.Count <= 0) return null;
            return result;
        });
        cacheKey = $"{this.appId}.binding.all";
        var bindings = await this.redisCache.GetOrCreateAsync(cacheKey, async () =>
        {
            var repository = this.dbFactory.Create(this.dbKey);
            var result = await repository.QueryAsync<Binding>();
            if (result.Count <= 0) return null;
            return result;
        });
        return (queues, bindings);
    }
    public virtual async Task<bool> Register(List<Queue> queues, List<Binding> bindings)
    {
        bool refresh = false;
        var repository = this.dbFactory.Create(this.dbKey);
        if (queues != null && queues.Count > 0)
        {
            await repository.Create<Queue>()
                .IgnoreInto().WithBulk(queues)
                .ExecuteAsync();
            await this.redisCache.RemoveAsync($"{this.appId}.queue.all");
            refresh = true;
        }
        if (bindings != null && bindings.Count > 0)
        {
            await repository.Create<Binding>()
                .IgnoreInto().WithBulk(bindings)
                .ExecuteAsync();
            await this.redisCache.RemoveAsync($"{this.appId}.binding.all");
            refresh = true;
        }
        return refresh;
    }
    public virtual async Task WriteLogs(List<ExecLog> logInfos)
    {
        var repository = this.dbFactory.CreateRepository(this.dbKey);
        await repository.CreateAsync<ExecLog>(logInfos);
    }
}