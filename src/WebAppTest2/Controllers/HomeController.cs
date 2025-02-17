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
        for (int i = 1; i < 10; i++)
        {
            var message = $"message-{i}";
            //await this.messageDriven.PublishAsync("cache.refresh", "1", message);
            var jsonResult = await this.messageDriven.RequestAsync("award.take", "1", new { AwardId = message, Quantity = i % 5 });
            Console.WriteLine($"Rpc Result: {jsonResult}");
            Thread.Sleep(20);
        }

        return TheaResponse.Succeed("ok");
    }
}
