using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Thea;
using Thea.Logging;
using Thea.MessageDriven;
using Trolley;
using Trolley.PostgreSql;
using WebAppTest.Domain.Models;
using WebAppTest.Domain.Services;
using WebAppTest.Dtos;

namespace WebAppTest.Controllers
{
    [ApiController]
    [Route("[controller]/[action]")]
    public class HomeController : ControllerBase
    {
        private readonly IOrmDbFactory dbFactory;
        private readonly IMessageDriven messageDriven;
        private readonly ILogger logger;
        public HomeController(IOrmDbFactory dbFactory, IMessageDriven messageDriven, ILogger<HomeController> logger)
        {
            this.dbFactory = dbFactory;
            this.messageDriven = messageDriven;
            this.logger = logger;
        }

        [HttpGet]
        public async Task<TheaResponse> Index()
        {
            Thread.Sleep(200);
            this.logger.LogTagInformation("Index", "111111------------");
            await this.messageDriven.PublishAsync("cache.refresh", "1", "111");
            return TheaResponse.Success;
        }
        [HttpGet]
        public async Task<TheaResponse> ChangeQueue(string queueId, int workloadTotal)
        {
            await this.messageDriven.ChangeQueue(queueId, workloadTotal);
            return TheaResponse.Success;
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
        public async Task<TheaResponse> RemoveCache(string cacheKey)
        {
            await this.messageDriven.PublishAsync("cache.refresh", "1", cacheKey);
            return TheaResponse.Succeed("ok");
        }
        [HttpPost]
        public async Task<TheaResponse> TakeAward(string awardId)
        {
            var result = await this.messageDriven.RequestAsync<AwardInfo, AwardInfo>("award.take", awardId, new AwardInfo { AwardId = awardId, Quantity = 2 });
            Console.WriteLine($"rpc.result:{result}");
            return TheaResponse.Succeed(result);
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
                    _ = Task.Run(() =>
                    {
                        var stopwatch = Stopwatch.StartNew();
                        stopwatch.Start();
                        this.messageDriven.Publish("award.take", message, new AwardInfo { AwardId = message, Quantity = i % 5 });
                        stopwatch.Stop();
                        Console.WriteLine($"Request time: {stopwatch.ElapsedMilliseconds}ms");
                    });
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
            this.messageDriven.Publish("award.take", awardId.ToString(), new AwardInfo { AwardId = awardId.ToString(), Quantity = awardId % 5 });
            stopwatch.Stop();
            Console.WriteLine($"Request time: {stopwatch.ElapsedMilliseconds}ms");
            return TheaResponse.Succeed("ok");
        }
    }
}
