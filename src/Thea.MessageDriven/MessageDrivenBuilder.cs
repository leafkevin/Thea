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
    public MessageDrivenBuilder UseProducer(params string[] exchanges)
    {
        this.messageDriven.UseProducer(exchanges);
        return this;
    }
    public MessageDrivenBuilder UseProducer(string exchange, bool isUseRpc)
    {
        this.messageDriven.UseProducer(exchange, isUseRpc);
        return this;
    }
    public MessageDrivenBuilder UseStatefulConsumer<TConsumer>(string exchange, string queue, Func<TConsumer, Delegate> consumerHandlerSelector, bool isNeedTransfer = false)
    {
        var consumer = ServiceProvider.GetService<TConsumer>();
        var methodInfo = consumerHandlerSelector.Invoke(consumer).Method;
        this.messageDriven.UseStatefulConsumer(exchange, queue, methodInfo, isNeedTransfer);
        return this;
    }
    public MessageDrivenBuilder UseSubscriber<TConsumer>(string exchange, string queue, Func<TConsumer, Delegate> consumerHandlerSelector, string routingKey = "#", bool isDelay = false)
    {
        var consumer = ServiceProvider.GetService<TConsumer>();
        var methodInfo = consumerHandlerSelector.Invoke(consumer).Method;
        this.messageDriven.UseSubscriber(exchange, queue, methodInfo, routingKey, isDelay);
        return this;
    }
    public MessageDrivenBuilder UseRpcConsumer()
    {
        this.messageDriven.UseRpcConsumer();
        return this;
    }
    public MessageDrivenBuilder UseStrategy(string exchange, Func<string, object, string> exchangeSelector)
    {
        this.messageDriven.UseStrategy(exchange, exchangeSelector);
        return this;
    }
    public MessageDrivenBuilder UseTraceId(Func<string> traceIdFetcher)
    {
        if (traceIdFetcher == null)
            throw new ArgumentNullException(nameof(traceIdFetcher));
        this.messageDriven.UseTraceIdFetcher(traceIdFetcher);
        return this;
    }
    public MessageDrivenBuilder UseLoadBalance(bool isForcePerInterval, int intervalMinutes = 10)
    {
        this.messageDriven.UseLoadBalance(isForcePerInterval, intervalMinutes);
        return this;
    }
}