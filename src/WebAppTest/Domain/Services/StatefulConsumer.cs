using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Thea;
using Thea.MessageDriven;
using Trolley;

namespace WebAppTest.Domain.Services;

public class StatefulConsumer
{
    private readonly IMessageDriven messageDriven;
    private readonly ILogger<StatefulConsumer> logger;
    private readonly IDistributedCache redisCache;
    private readonly ConcurrentDictionary<string, long> lastIds = new();

    public StatefulConsumer(IMessageDriven messageDriven, IDistributedCache redisCache, ILogger<StatefulConsumer> logger)
    {
        this.messageDriven = messageDriven;
        this.redisCache = redisCache;
        this.logger = logger;
    }
    public Task<string> RemoveCache(string key)
    {
        //Thread.Sleep(10);
        Console.WriteLine($"RemoveCache: {key}");
        //this.logger.LogInformation("2222222");
        return Task.FromResult(key);
    }
    public Task<AwardInfo> TakeAward(AwardInfo awardInfo)
    {
        //Thread.Sleep(10);
        Console.WriteLine($"TakeAward: 我要领奖 {awardInfo.ToJson()}    --   {awardInfo.AwardId}");
        //this.messageDriven.Request<string, string>("cache.refresh", awardInfo.AwardId, awardInfo.AwardId);
        this.messageDriven.PublishAsync("cache.refresh", awardInfo.AwardId, awardInfo.AwardId);
        return Task.FromResult(awardInfo);
    }
    public Task<TheaResponse<AwardInfo>> IssueAward(AwardInfo awardInfo)
    {
        //Thread.Sleep(10);
        //Console.WriteLine($"IssueAward: 我已领取 {awardInfo.ToJson()}");
        return Task.FromResult(TheaResponse.Succeed(awardInfo));
    }
    public async Task<long> UpdateSequence(string senceKey)
    {
        var currentId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        currentId = this.lastIds.AddOrUpdate(senceKey, currentId, (key, lastId) => lastId + 1);
        (_, var sequence) = await this.redisCache.GetAsync<long>(senceKey);
        if (currentId <= sequence)
            currentId = sequence + 1;
        this.lastIds[senceKey] = currentId;
        await this.redisCache.SetAsync(senceKey, currentId);
        return currentId;
    }
}
public class AwardInfo
{
    public string AwardId { get; set; }
    public int Quantity { get; set; }
    public string RecipientId { get; set; }
}