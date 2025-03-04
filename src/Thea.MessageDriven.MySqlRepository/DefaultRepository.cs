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
    public virtual async Task<(List<Queue>, List<Binding>)> GetConfigInfo(bool useCache = true)
    {
        var cacheKey = $"{this.appId}.queue.all";
        if (!useCache) await this.redisCache.RemoveAsync(cacheKey);
        var queues = await this.redisCache.GetOrCreateAsync(cacheKey, async () =>
        {
            var repository = this.dbFactory.Create(this.dbKey);
            var result = await repository.QueryAsync<Queue>(f => f.IsEnabled);
            if (result.Count <= 0) return null;
            return result;
        });
        cacheKey = $"{this.appId}.binding.all";
        if (!useCache) await this.redisCache.RemoveAsync(cacheKey);
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
            refresh = true;
        }
        if (bindings != null && bindings.Count > 0)
        {
            await repository.Create<Binding>()
                .IgnoreInto().WithBulk(bindings)
                .ExecuteAsync();
            refresh = true;
        }
        //这里不能更新缓存，一更新缓存，只有生产者的组件，在队列没有创建好前，获得了这个配置，
        //消息会被发送到一个不存在的队列，导致消息丢失
        return refresh;
    }
    public virtual async Task ChangeQueue(string queueId, int workloadTotal)
    {
        var repository = this.dbFactory.Create(this.dbKey);
        await repository.UpdateAsync<Queue>(new { QueueId = queueId, WorkloadTotal = workloadTotal });
        var cacheKey = $"{this.appId}.queue.all";
        await this.redisCache.RemoveAsync(cacheKey);
    }
    public virtual async Task UpdateCache()
    {
        var cacheKey = $"{this.appId}.queue.all";
        await this.redisCache.RemoveAsync(cacheKey);
        cacheKey = $"{this.appId}.binding.all";
        await this.redisCache.RemoveAsync(cacheKey);
    }
    public virtual async Task WriteLogs(List<ExecLog> logInfos)
    {
        var repository = this.dbFactory.CreateRepository(this.dbKey);
        await repository.CreateAsync<ExecLog>(logInfos);
    }
}