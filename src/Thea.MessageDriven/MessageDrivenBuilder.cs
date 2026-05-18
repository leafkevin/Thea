using Microsoft.Extensions.DependencyInjection;
using System;

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
    public MessageDrivenBuilder UseProducer(params string[] exchanges)
    {
        this.messageDriven.UseProducer(exchanges);
        return this;
    }
    public MessageDrivenBuilder UseStatefulConsumer<TConsumer>(string exchange, string queue, Func<TConsumer, Delegate> consumerHandlerSelector, bool isNeedTransfer = false, bool isSingleActiveConsumer = true, bool isQuorumQueue = true)
    {
        var consumer = ServiceProvider.GetService<TConsumer>();
        var methodInfo = consumerHandlerSelector.Invoke(consumer).Method;
        this.messageDriven.UseStatefulConsumer(exchange, queue, methodInfo, isNeedTransfer, isSingleActiveConsumer, isQuorumQueue);
        return this;
    }
    public MessageDrivenBuilder UseSubscriber<TConsumer>(string exchange, string queue, Func<TConsumer, Delegate> consumerHandlerSelector, string routingKey = "#", bool isDelay = false, bool isQuorumQueue = true)
    {
        var consumer = ServiceProvider.GetService<TConsumer>();
        var methodInfo = consumerHandlerSelector.Invoke(consumer).Method;
        this.messageDriven.UseSubscriber(exchange, queue, methodInfo, routingKey, isDelay, isQuorumQueue);
        return this;
    }
    public MessageDrivenBuilder UseRpcConsumer()
    {
        this.messageDriven.UseRpcConsumer();
        return this;
    }
}