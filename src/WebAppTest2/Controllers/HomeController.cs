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
        for (int i = 1; i < 100000000; i++)
        {
            var message = $"message-{i}";
            await this.messageDriven.PublishAsync("cache.refresh", "1", message);
            await this.messageDriven.PublishAsync("award.take", "1", new { AwardId = message, Quantity = i % 5 });
            Thread.Sleep(20);
        }

        return TheaResponse.Succeed("ok");
    }
}
