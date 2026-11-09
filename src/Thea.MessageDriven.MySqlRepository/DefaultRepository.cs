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
    private readonly string cacheKey;
    private readonly IOrmDbFactory dbFactory;
    private readonly IDistributedCache redisCache;

    public DefaultRepository(IServiceProvider serviceProvider)
    {
        this.dbFactory = serviceProvider.GetService<IOrmDbFactory>();
        var configuration = serviceProvider.GetService<IConfiguration>();
        this.dbKey = configuration.GetValue<string>("MessageDriven:DbKey");
        this.cacheKey = configuration.GetValue("MessageDriven:CacheKey", "thea.messageDriven.all");
        this.redisCache = serviceProvider.GetService<IDistributedCache>();
    }
    public virtual async Task<(List<Queue>, List<Binding>)> GetSettings(bool useCache = true)
    {
        if (!useCache) await this.redisCache.RemoveAsync(this.cacheKey);
        var cacheValue = await this.redisCache.GetOrCreateAsync(this.cacheKey, async () =>
        {
            var repository = this.dbFactory.Create(this.dbKey);
            using var reader = await repository.QueryMultipleAsync(t =>
            {
                t.Query<Queue>();
                t.From<Binding, Queue>()
                    .Where((a, b) => a.QueueId == b.QueueId)
                    .SelectTo<Binding>().ToList();
            });
            var queues = await reader.ReadAsync<Queue>();
            var bindings = await reader.ReadAsync<Binding>();
            return new MessageDrivenCache { Queues = queues, Bindings = bindings };
        });
        return (cacheValue.Queues, cacheValue.Bindings);
    }
    public virtual async Task Register(List<Queue> queues, List<Binding> bindings)
    {
        var repository = this.dbFactory.Create(this.dbKey);
        if (queues != null && queues.Count > 0)
        {
            await repository.Create<Queue>()
                .WithBulk(queues)
                .OnDuplicateKeyUpdate()
                .Set(f => new
                {
                    AppId = f.Values(f.AppId),
                    IsStateful = f.Values(f.IsStateful),
                    IsTransfer = f.Values(f.IsTransfer)
                })
                .ExecuteAsync();
        }
        if (bindings != null && bindings.Count > 0)
        {
            await repository.Create<Binding>()
                .WithBulk(bindings)
                .OnDuplicateKeyUpdate()
                .Set(f => new
                {
                    BindType = f.Values(f.BindType),
                    BindingKey = f.Values(f.BindingKey),
                    IsDelay = f.Values(f.IsDelay)
                })
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
                myQueue.IsConfirmed,
                myQueue.IsLogEnabled
            })
            .Where(f => f.QueueId == myQueue.QueueId)
            .ExecuteAsync();
        await this.redisCache.RemoveAsync(this.cacheKey);
    }
    public virtual async Task Confirm(string appId)
    {
        var repository = this.dbFactory.Create(this.dbKey);
        await repository.Update<Queue>()
            .Set(f => f.IsConfirmed, true)
            .Where(f => f.AppId == appId && f.IsConfirmed == false)
            .ExecuteAsync();
        await this.redisCache.RemoveAsync(this.cacheKey);
    }
    public virtual async Task WriteLogs(List<ExecLog> logInfos)
    {
        var repository = this.dbFactory.Create(this.dbKey);
        await repository.CreateAsync<ExecLog>(logInfos);
    }
    class MessageDrivenCache
    {
        public List<Queue> Queues { get; set; }
        public List<Binding> Bindings { get; set; }
    }
}