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
        for (int i = 1; i < 1000000; i++)
        {
            var message = $"message-{i}";
            //await this.messageDriven.PublishAsync("cache.refresh", "1", message);
            Console.WriteLine($"Request: {message}");
            var jsonResult = await this.messageDriven.RequestAsync("award.take", i.ToString(), new { AwardId = message, Quantity = i % 5 });
            Console.WriteLine($"Rpc Result: {jsonResult}");
            Thread.Sleep(50);
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
