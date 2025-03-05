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
                _ = this.messageDriven.PublishAsync("cache.refresh", "1", message);
                _ = this.messageDriven.PublishAsync("award.take", i.ToString(), new { AwardId = message, Quantity = i % 5 });
            }
            Thread.Sleep(1000);
        }
        return TheaResponse.Succeed("ok");
    }
    [HttpPost]
    public async Task<TheaResponse> PublishMessage(int awardId)
    {
        await this.messageDriven.PublishAsync("cache.refresh", "1", new { AwardId = awardId.ToString(), Quantity = awardId % 5 });
        await this.messageDriven.PublishAsync("award.take", awardId.ToString(), new { AwardId = awardId.ToString(), Quantity = awardId % 5 });
        return TheaResponse.Succeed("ok");
    }
}