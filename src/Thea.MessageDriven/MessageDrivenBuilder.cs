using Microsoft.Extensions.DependencyInjection;
using System;
using System.Reflection;

namespace Thea.MessageDriven;

public class MessageDrivenBuilder
{
    private readonly MessageDrivenService messageDriven;
    private readonly IServiceProvider serviceProvider;

    public MessageDrivenBuilder(IServiceProvider serviceProvider)
    {
        this.serviceProvider = serviceProvider;
        this.messageDriven = serviceProvider.GetRequiredService<MessageDrivenService>();
    }
    public MessageDrivenBuilder UseRepository(Func<IServiceProvider, IMessageDrivenRepository> repositoryInitializer)
    {
        if (repositoryInitializer == null)
            throw new ArgumentNullException(nameof(repositoryInitializer));
        var repository = repositoryInitializer.Invoke(this.serviceProvider);
        this.messageDriven.UseRepository(repository);
        return this;
    }
    public MessageDrivenBuilder UseProducer()
    {
        this.messageDriven.UseProducer();
        return this;
    }
    public MessageDrivenBuilder UseStatefulConsumer<TConsumer>(string exchange, string queue, MethodInfo consumerHandler, bool isSingleActiveConsumer = true, bool isQuorumQueue = true)
    {
        this.messageDriven.UseStatefulConsumer(exchange, queue, consumerHandler, isSingleActiveConsumer, isQuorumQueue);
        return this;
    }
    public MessageDrivenBuilder UseSubscriber<TConsumer>(string queue, MethodInfo consumerHandler, bool isQuorumQueue = true)
    {
        this.messageDriven.UseSubscriber(queue, consumerHandler, isQuorumQueue);
        return this;
    }
    public MessageDrivenBuilder UseSubscriber<TConsumer>(string exchange, string queue, MethodInfo consumerHandler, bool isDelay = false, bool isQuorumQueue = true)
    {
        this.messageDriven.UseSubscriber(exchange, queue, consumerHandler, isDelay, isQuorumQueue);
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
    internal MessageDrivenService Build() => this.messageDriven;
}