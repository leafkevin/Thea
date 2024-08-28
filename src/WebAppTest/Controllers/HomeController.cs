using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;
using Thea;
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
        public HomeController(IOrmDbFactory dbFactory)
        {
            this.dbFactory = dbFactory;
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
    }
}
