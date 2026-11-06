using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading;
using System.Threading.Tasks;
using Thea;
using Thea.MessageDriven;

namespace WebAppTest.Controllers;

[ApiController]
[Route("[controller]/[action]")]
public class SequenceController : ControllerBase
{
    private readonly IMessageDriven messageDriven;

    public SequenceController(IMessageDriven messageDriven)
    {
        this.messageDriven = messageDriven;
    }
    [HttpGet]
    public async Task<TheaResponse> Generate(string gameId)
    {
        var result = await this.messageDriven.RequestAsync<string, string>("sequence", gameId, gameId);
        return TheaResponse.Succeed(result);
    }
    [HttpGet]
    public async Task<TheaResponse> GenerateSequences(int totalCount = 100000)
    {
        //int current = 0;
        var queueName = this.messageDriven.RpcQueueName;
        for (int i = 1; i <= totalCount; i++)
        {
            var messageId = $"{i}";
            var gameId = $"{i % 5}";
            _ = this.messageDriven.PublishRpcAsync(queueName, messageId, "sequence", gameId, messageId);
            //Interlocked.Increment(ref current);
            //Console.WriteLine(Volatile.Read(ref current));
        }
        return TheaResponse.Success;
    }
}
