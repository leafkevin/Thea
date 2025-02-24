using System;
using Microsoft.Extensions.DependencyInjection;

namespace Thea.MessageDriven;

public class MessageDrivenBuilder
{
    private readonly MessageDrivenService messageDriven;
    public IServiceProvider ServiceProvider { get; private set; }

    public MessageDrivenBuilder(IServiceProvider serviceProvider)
    {
        this.ServiceProvider = serviceProvider;
        this.messageDriven = serviceProvider.GetService<IMessageDriven>() as MessageDrivenService;
    }
    public MessageDrivenBuilder UseRepository(IMessageDrivenRepository repository)
    {
        this.messageDriven.UseRepository(repository);
        return this;
    }
    public MessageDrivenBuilder UseRepository<TRepository>() where TRepository : IMessageDrivenRepository, new()
    {
        this.messageDriven.UseRepository(new TRepository());
        return this;
    }
    public MessageDrivenBuilder UseProducer(params string[] clusterIds)
    {
        this.messageDriven.UseProducer(clusterIds);
        return this;
    }
    public MessageDrivenBuilder UseProducer(string clusterId, bool isUseRpc)
    {
        this.messageDriven.UseProducer(clusterId, isUseRpc);
        return this;
    }
    public MessageDrivenBuilder UseStatefulConsumer<TConsumer>(string exchange, string queue, Func<TConsumer, Delegate> consumerHandlerSelector)
    {
        var consumer = ServiceProvider.GetService<TConsumer>();
        var methodInfo = consumerHandlerSelector.Invoke(consumer).Method;
        this.messageDriven.UseStatefulConsumer(exchange, queue, methodInfo);
        return this;
    }
    public MessageDrivenBuilder UseSubscriber<TConsumer>(string clusterId, string queue, Func<TConsumer, Delegate> consumerHandlerSelector, string routingKey = "#", bool isDelay = false)
    {
        var consumer = ServiceProvider.GetService<TConsumer>();
        var methodInfo = consumerHandlerSelector.Invoke(consumer).Method;
        this.messageDriven.UseSubscriber(clusterId, queue, methodInfo, routingKey, isDelay);
        return this;
    }
    public MessageDrivenBuilder UseStrategy(string exchange, ExchangeRoutingSelector exchangeRoutingKeySelector)
    {
        this.messageDriven.UseStrategy(exchange, exchangeRoutingKeySelector);
        return this;
    }
}