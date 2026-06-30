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
    public MessageDrivenBuilder UseStatefulConsumer<TConsumer>(string exchange, string queue, Func<TConsumer, Delegate> consumerHandlerSelector, bool isSingleActiveConsumer = true, bool isQuorumQueue = true)
    {
        var consumer = ServiceProvider.GetService<TConsumer>();
        var methodInfo = consumerHandlerSelector.Invoke(consumer).Method;
        this.messageDriven.UseStatefulConsumer(exchange, queue, methodInfo, isSingleActiveConsumer, isQuorumQueue);
        return this;
    }
    public MessageDrivenBuilder UseSubscriber<TConsumer>(string queue, Func<TConsumer, Delegate> consumerHandlerSelector, bool isQuorumQueue = true)
    {
        var consumer = ServiceProvider.GetService<TConsumer>();
        var methodInfo = consumerHandlerSelector.Invoke(consumer).Method;
        this.messageDriven.UseSubscriber(queue, methodInfo, isQuorumQueue);
        return this;
    }
    public MessageDrivenBuilder UseSubscriber<TConsumer>(string exchange, string queue, Func<TConsumer, Delegate> consumerHandlerSelector, bool isDelay = false, bool isQuorumQueue = true)
    {
        var consumer = ServiceProvider.GetService<TConsumer>();
        var methodInfo = consumerHandlerSelector.Invoke(consumer).Method;
        this.messageDriven.UseSubscriber(exchange, queue, methodInfo, isDelay, isQuorumQueue);
        return this;
    }
    public MessageDrivenBuilder UseTransfer(string fromExchange, string toExchange, string routingKey)
    {
        this.messageDriven.UseTransfer(fromExchange, toExchange, routingKey);
        return this;
    }
    public MessageDrivenBuilder UseRpcConsumer()
    {
        this.messageDriven.UseRpcConsumer();
        return this;
    }
}