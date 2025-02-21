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

    [HttpPost]
    public async Task<TheaResponse> PublishMessages()
    {
        for (int i = 1867250; i < 999999999; i++)
        {
            var message = $"message-{i}";
            await this.messageDriven.PublishAsync("cache.refresh", "1", message);
            await this.messageDriven.PublishAsync("award.take", i.ToString(), new { AwardId = message, Quantity = i % 5 });
            Console.WriteLine($"Rpc Result: {message}");
        }
        return TheaResponse.Succeed("ok");
    }
    [HttpPost]
    public async Task<TheaResponse> PublishMessage(int awardId)
    {
        //await this.messageDriven.PublishAsync("cache.refresh", "1", message);
        var jsonResult = await this.messageDriven.RequestAsync("award.take", awardId.ToString(), new { AwardId = awardId.ToString(), Quantity = awardId % 5 });
        Console.WriteLine($"Rpc Result: {jsonResult}");
        return TheaResponse.Succeed("ok");
    }
}
