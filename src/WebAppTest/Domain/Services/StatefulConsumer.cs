using System;
using System.Threading.Tasks;
using Thea;
using Thea.MessageDriven;

namespace WebAppTest.Domain.Services;

public class StatefulConsumer
{
    private readonly IMessageDriven messageDriven;
    public StatefulConsumer(IMessageDriven messageDriven)
    {
        this.messageDriven = messageDriven;
    }
    public Task RemoveCache(string key)
    {
        //Console.WriteLine($"RemoveCache: {key}");
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