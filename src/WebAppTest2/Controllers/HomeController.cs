using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Thea;
using Thea.MessageDriven;

namespace WebAppTest2.Controllers;

[ApiController]
[Route("[controller]/[action]")]
public class HomeController : ControllerBase
{
    private readonly IMessageDriven messageDriven;

    public HomeController(IMessageDriven messageDriven)
    {
        this.messageDriven = messageDriven;
    }

    [HttpGet]
    public async Task<TheaResponse> PublishMessages(int tps = 1000)
    {
        for (int i = 0; i < 999999999; i++)
        {
            for (int j = 0; j < tps; j++)
            {
                var message = $"message-{i}-{j}";
                //_ = this.messageDriven.PublishAsync("cache.refresh", "1", message);
                //_ = this.messageDriven.PublishAsync("award.take", i.ToString(), new { AwardId = message, Quantity = i % 5 });

                var stopwatch = Stopwatch.StartNew();
                stopwatch.Start();
                await this.messageDriven.RequestAsync<AwardInfo, AwardInfo>("award.take", i.ToString(), new AwardInfo { AwardId = message, Quantity = i % 5 });
                stopwatch.Stop();
                Console.WriteLine($"Request time: {stopwatch.ElapsedMilliseconds}ms");
            }
            Thread.Sleep(1000);
        }
        return TheaResponse.Succeed("ok");
    }
    [HttpGet]
    public async Task<TheaResponse> PublishMessage(int awardId)
    {
        //await this.messageDriven.PublishAsync("cache.refresh", "1", new { AwardId = awardId.ToString(), Quantity = awardId % 5 });
        var stopwatch = Stopwatch.StartNew();
        stopwatch.Start();
        await this.messageDriven.RequestAsync<AwardInfo, AwardInfo>("award.take", awardId.ToString(), new AwardInfo { AwardId = awardId.ToString(), Quantity = awardId % 5 });
        stopwatch.Stop();
        Console.WriteLine($"Request time: {stopwatch.ElapsedMilliseconds}ms");
        return TheaResponse.Succeed("ok");
    }
    public class AwardInfo
    {
        public required string AwardId { get; set; }
        public int Quantity { get; set; }
        public string? RecipientId { get; set; }
    }
}