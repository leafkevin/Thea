using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading.Tasks;

namespace Thea.MessageDriven;

public class MessageDrivenBuilder
{
    private readonly MessageDrivenService service;
    private readonly IServiceProvider serviceProvider;
    internal MessageDrivenBuilder(IServiceProvider serviceProvider)
    {
        this.serviceProvider = serviceProvider;
        this.service = serviceProvider.GetService<IMessageDriven>() as MessageDrivenService;
    }

    public MessageDrivenBuilder Create(string dbKey, string nodeId)
    {
        this.service.DbKey = dbKey;
        this.service.HostName = nodeId;
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
    public MessageDrivenBuilder AddConsumer<TConsumer>(string clusterId, Func<TConsumer, Func<string, Task<TheaResponse>>> consumerHandler)
    {
        var consumer = serviceProvider.GetService<TConsumer>();
        return this.AddConsumer(clusterId, consumerHandler.Invoke(consumer));
    }
}