using System;
using System.Threading.Tasks;

namespace Thea.MessageDriven;

public delegate (string, string) ExchangeRoutingSelector(string exchange, string routingKey, object message);

public interface IMessageDriven
{
    void Start();
    void Shutdown();

    Task ChangeQueue(string queue, int prefetchCount);

    void Publish<TMessage>(string exchange, string routingKey, TMessage message);
    Task PublishAsync<TMessage>(string exchange, string routingKey, TMessage message);
    string Request<TMessage>(string exchange, string routingKey, TMessage message);
    Task<string> RequestAsync<TMessage>(string exchange, string routingKey, TMessage message);
    void Schedule<TMessage>(string exchange, string routingKey, TMessage message, DateTime enqueueTimeUtc);
    Task ScheduleAsync<TMessage>(string exchange, string routingKey, TMessage message, DateTime enqueueTimeUtc);
}
