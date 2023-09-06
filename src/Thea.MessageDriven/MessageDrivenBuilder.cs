using System;
using System.Threading.Tasks;

namespace Thea.MessageDriven;

public class MessageDrivenBuilder
{
    private readonly MessageDrivenService service;
    internal MessageDrivenBuilder(MessageDrivenService service) => this.service = service;

    public MessageDrivenBuilder Create(string dbKey, string nodeId)
    {
        this.service.DbKey = dbKey;
        this.service.NodeId = nodeId;
        return this;
    }
    public MessageDrivenBuilder AddProducer(string clusterId, bool isUseRpc = false)
    {
        this.service.AddProducer(clusterId, isUseRpc);
        return this;
    }
    public MessageDrivenBuilder AddConsumer(string clusterId, Func<string, Task<TheaResponse>> consumerHandler)
    {
        Func<TheaMessage, Task<TheaResponse>> theaConsumerHandler = theaMessage => consumerHandler.Invoke(theaMessage.Message);
        this.service.AddConsumer(clusterId, theaConsumerHandler);
        return this;
    }
}