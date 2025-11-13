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
    public virtual async Task<List<Setting>> GetSettings(bool useCache = true)
    {
        var cacheKey = $"{this.appId}.settings.all";
        if (!useCache) await this.redisCache.RemoveAsync(cacheKey);
        var settings = await this.redisCache.GetOrCreateAsync(cacheKey, async () =>
        {
            var repository = this.dbFactory.Create(this.dbKey);
            var result = await repository.QueryAsync<Setting>(f => f.IsEnabled);
            if (result.Count <= 0) return null;
            return result;
        });
        return settings;
    }
    public virtual async Task<bool> Register(List<Setting> settings)
    {
        bool refresh = false;
        var repository = this.dbFactory.Create(this.dbKey);
        if (settings != null && settings.Count > 0)
        {
            await repository.Create<Setting>()
                .IgnoreInto().WithBulk(settings)
                .ExecuteAsync();
            refresh = true;
        }
        //这里不能更新缓存，一更新缓存，只有生产者的组件，在队列没有创建好前，获得了这个配置，
        //消息会被发送到一个不存在的队列，导致消息丢失
        return refresh;
    }
    public virtual async Task Change(string queueId, int workloadTotal)
    {
        var repository = this.dbFactory.Create(this.dbKey);
        await repository.UpdateAsync<Setting>(new { QueueId = queueId, WorkloadTotal = workloadTotal });
        var cacheKey = $"{this.appId}.settings.all";
        await this.redisCache.RemoveAsync(cacheKey);
    }
    public virtual async Task WriteLogs(List<ExecLog> logInfos)
    {
        var repository = this.dbFactory.CreateRepository(this.dbKey);
        await repository.CreateAsync<ExecLog>(logInfos);
    }
}