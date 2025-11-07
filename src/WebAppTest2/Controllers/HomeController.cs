using System.Diagnostics;
using System.Threading.Tasks;
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
                await this.messageDriven.PublishAsync("cache.refresh", "1", message);
                var result = await this.messageDriven.RequestAsync<AwardInfo, AwardInfo>("award.take", i.ToString(), new AwardInfo { AwardId = message, Quantity = i % 5 });
                Console.WriteLine($"Request {result.ToJson()}, time: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
            }
            Thread.Sleep(1000);
        }
        return TheaResponse.Succeed("ok");
    }
    [HttpGet]
    public async Task<TheaResponse> RequestMessage(int awardId)
    {
        //await this.messageDriven.PublishAsync("cache.refresh", "1", new { AwardId = awardId.ToString(), Quantity = awardId % 5 });
        var stopwatch = Stopwatch.StartNew();
        stopwatch.Start();
        var result = await this.messageDriven.RequestAsync<AwardInfo, AwardInfo>("award.take", awardId.ToString(), new AwardInfo { AwardId = awardId.ToString(), Quantity = awardId % 5 });
        stopwatch.Stop();
        Console.WriteLine($"Request time: {stopwatch.ElapsedMilliseconds}ms");
        return TheaResponse.Succeed(result);
    }
    public class AwardInfo
    {
        public required string AwardId { get; set; }
        public int Quantity { get; set; }
        public string? RecipientId { get; set; }
    }
}