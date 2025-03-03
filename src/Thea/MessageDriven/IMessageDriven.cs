using System;
using System.Threading.Tasks;

namespace Thea.MessageDriven;

public interface IMessageDriven
{
    void Start();
    void Shutdown();

    Task ChangeQueue(string queue, int prefetchCount);

    void Publish<TMessage>(string exchange, string routingKey, TMessage message);
    Task PublishAsync<TMessage>(string exchange, string routingKey, TMessage message);
    TResponse Request<TMessage, TResponse>(string exchange, string routingKey, TMessage message);
    Task<TResponse> RequestAsync<TMessage, TResponse>(string exchange, string routingKey, TMessage message);
    void Schedule<TMessage>(string exchange, string routingKey, TMessage message, DateTime enqueueTimeUtc);
    Task ScheduleAsync<TMessage>(string exchange, string routingKey, TMessage message, DateTime enqueueTimeUtc);
}