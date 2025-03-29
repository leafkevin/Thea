using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Thea;
using Thea.MessageDriven;

namespace WebAppTest.Domain.Services;

public class StatefulConsumer
{
    private readonly IMessageDriven messageDriven;
    private readonly ILogger<StatefulConsumer> logger;
    public StatefulConsumer(IMessageDriven messageDriven, ILogger<StatefulConsumer> logger)
    {
        this.messageDriven = messageDriven;
        this.logger = logger;
    }
    public Task RemoveCache(string key)
    {
        //Console.WriteLine($"RemoveCache: {key}");
        this.logger.LogInformation("2222222");
        return Task.CompletedTask;
    }
    public Task<AwardInfo> TakeAward(AwardInfo awardInfo)
    {
        //Console.WriteLine($"TakeAward: 我要领奖 {awardInfo.ToJson()}    --   {awardInfo.AwardId}");
        this.messageDriven.Publish("award.issue", awardInfo.AwardId, awardInfo);
        return Task.FromResult(awardInfo);
    }
    public Task<TheaResponse> IssueAward(AwardInfo awardInfo)
    {
        //Console.WriteLine($"IssueAward: 我已领取 {awardInfo.ToJson()}");
        return Task.FromResult(TheaResponse.Succeed(awardInfo));
    }
}
public class AwardInfo
{
    public string AwardId { get; set; }
    public int Quantity { get; set; }
    public string RecipientId { get; set; }
}