using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;
using Thea;
using Thea.MessageDriven;
using Trolley;
using Trolley.PostgreSql;
using WebAppTest.Domain.Models;
using WebAppTest.Dtos;

namespace WebAppTest.Controllers
{
    [ApiController]
    [Route("[controller]/[action]")]
    public class HomeController : ControllerBase
    {
        private readonly IOrmDbFactory dbFactory;
        private readonly IMessageDriven messageDriven;
        public HomeController(IOrmDbFactory dbFactory, IMessageDriven messageDriven)
        {
            this.dbFactory = dbFactory;
            this.messageDriven = messageDriven;
        }

        [HttpGet]
        public string Index()
        {
            return "ok";
        }
        [HttpPost]
        public async Task<TheaResponse> GetLookupValueList([FromBody] QueryPagedRequest request)
        {
            var repository = this.dbFactory.Create();
            var result = await repository.From<Lookup, LookupValue>()
                .InnerJoin((x, y) => x.LookupId == y.LookupId)
                .Where((x, y) => x.IsEnabled && y.IsEnabled)
                .And(!string.IsNullOrEmpty(request.QueryText), (x, y) => x.LookupId.Contains(request.QueryText)
                    || x.LookupName.Contains(request.QueryText) || y.LookupText.Contains(request.QueryText))
                .Select((x, y) => new
                {
                    x.LookupId,
                    x.LookupName,
                    y.LookupText,
                    LookupValue = y.Value,
                    y.Sequence,
                    y.UpdatedAt
                })
                .Page(request.PageNumber, request.PageSize)
                .ToPageListAsync();
            return TheaResponse.Succeed(result);
        }
        [HttpPost]
        public async Task<TheaResponse> RemoveCache([FromBody] string cacheKey)
        {
            await this.messageDriven.PublishAsync("cache.refresh", "1", cacheKey);
            return TheaResponse.Succeed("ok");
        }
    }
}
