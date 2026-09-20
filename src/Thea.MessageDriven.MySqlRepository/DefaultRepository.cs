using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Trolley;
using Trolley.MySqlConnector;
using static System.Runtime.InteropServices.JavaScript.JSType;

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
        var cacheKey = "thea.settings.all";
        if (!useCache) await this.redisCache.RemoveAsync(cacheKey);
        return await this.redisCache.GetOrCreateAsync(cacheKey, async () =>
        {
            var repository = this.dbFactory.Create(this.dbKey);
            return await repository.From<Binding, Queue>()
                .Where((a, b) => a.QueueId == b.QueueId)
                .Select((a, b) => new Setting
                {
                    ExchangeId = a.ExchangeId,
                    QueueId = a.QueueId,
                    AppId = b.AppId,
                    BindType = a.BindType,
                    WorkloadTotal = b.WorkloadTotal,
                    PrefetchCount = b.PrefetchCount,
                    IsStateful = a.IsStateful,
                    IsEnabled = b.IsEnabled
                })
                .ToListAsync();
        });
    }
    public virtual async Task Register(List<Queue> queues, List<Binding> bindings)
    {
        var repository = this.dbFactory.Create(this.dbKey);
        if (queues != null && queues.Count > 0)
        {
            await repository.Create<Queue>()
                .IgnoreInto()
                .WithBulk(queues)
                .ExecuteAsync();
        }
        if (bindings != null && bindings.Count > 0)
        {
            await repository.Create<Binding>()
                .WithBulk(bindings)
                .OnDuplicateKeyUpdate(t => t.Set(f => new
                {
                    BindType = t.Values(f.BindType),
                    IsStateful = t.Values(f.IsStateful),
                    IsDelay = t.Values(f.IsDelay)
                }))
                .ExecuteAsync();
        }
    }
    public virtual async Task Change(Queue myQueue)
    {
        var repository = this.dbFactory.Create(this.dbKey);
        await repository.Update<Queue>()
            .Set(new
            {
                myQueue.WorkloadTotal,
                myQueue.PrefetchCount,
                myQueue.IsLogEnabled
            })
            .Where(f => f.QueueId == myQueue.QueueId)
            .ExecuteAsync();
        var cacheKey = "thea.queue.all";
        await this.redisCache.RemoveAsync(cacheKey);
    }
    public virtual async Task WriteLogs(List<ExecLog> logInfos)
    {
        var repository = this.dbFactory.CreateRepository(this.dbKey);
        await repository.CreateAsync<ExecLog>(logInfos);
    }
}