using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading.Tasks;
using Trolley;

namespace Thea.MessageDriven;

public class MessageDrivenBuilder
{
    private readonly MessageDrivenService messageDriven;
    private readonly IServiceProvider serviceProvider;

    public MessageDrivenBuilder(IServiceProvider serviceProvider)
    {
        this.serviceProvider = serviceProvider;
        this.messageDriven = serviceProvider.GetService<IMessageDriven>() as MessageDrivenService;
    }

    public MessageDrivenBuilder Create(string dbKey, string hostName = null)
    {
        this.messageDriven.DbKey = dbKey;
        this.messageDriven.HostName = hostName;

        var dbFactory = this.serviceProvider.GetService<IOrmDbFactory>();
        dbFactory.Configure<ModelConfiguration>(dbKey);
        dbFactory.Build();
        return this;
    }
    public MessageDrivenBuilder AddProducer(string clusterId, bool isUseRpc = false)
    {
        this.messageDriven.AddProducer(clusterId, isUseRpc);
        return this;
    }
    public MessageDrivenBuilder AddRpcReplyConsumer(string clusterId)
    {
        this.messageDriven.AddRpcReplyConsumer(clusterId);
        return this;
    }
    public MessageDrivenBuilder AddDelayProducer(string clusterId)
    {
        this.messageDriven.AddDelayProducer(clusterId);
        return this;
    }
    public MessageDrivenBuilder AddStatefulConsumer<TParameters>(string clusterId, Func<TParameters, Task> consumerHandler)
    {
        this.messageDriven.AddStatefulConsumer(clusterId, consumerHandler);
        return this;
    }
    public MessageDrivenBuilder AddStatefulConsumer<TConsumer>(string clusterId, Func<TConsumer, Delegate> consumerHandlerSelector)
    {
        var consumer = serviceProvider.GetService<TConsumer>();
        var methodInfo = consumerHandlerSelector.Invoke(consumer).Method;
        this.messageDriven.AddStatefulConsumer(clusterId, consumer, methodInfo);
        return this;
    }
    public MessageDrivenBuilder AddSubscriber<TParameters>(string clusterId, string queue, Func<TParameters, Task> consumerHandler, string routingKey = "#", bool isDelay = false)
    {
        this.messageDriven.AddSubscriber(clusterId, queue, consumerHandler, routingKey, isDelay);
        return this;
    }
    public MessageDrivenBuilder AddSubscriber<TConsumer>(string clusterId, string queue, Func<TConsumer, Delegate> consumerHandlerSelector, string routingKey = "#", bool isDelay = false)
    {
        var consumer = serviceProvider.GetService<TConsumer>();
        var methodInfo = consumerHandlerSelector.Invoke(consumer).Method;
        this.messageDriven.AddSubscriber(clusterId, queue, consumer, methodInfo, routingKey, isDelay);
        return this;
    }
}