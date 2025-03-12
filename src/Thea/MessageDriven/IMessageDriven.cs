using System;
using System.Threading.Tasks;

namespace Thea.MessageDriven;

public interface IMessageDriven
{
    string ServiceId { get; }
    void Start();
    void Shutdown();

    void Publish<TMessage>(string exchange, string routingKey, TMessage message);
    Task PublishAsync<TMessage>(string exchange, string routingKey, TMessage message);
    void PublishRpc<TMessage>(string serviceId, string messageId, string exchange, string routingKey, TMessage message);
    Task PublishRpcAsync<TMessage, TResponse>(string serviceId, string messageId, string exchange, string routingKey, TMessage message);
    TResponse Request<TMessage, TResponse>(string exchange, string routingKey, TMessage message);
    Task<TResponse> RequestAsync<TMessage, TResponse>(string exchange, string routingKey, TMessage message);
    void Schedule<TMessage>(string exchange, string routingKey, TMessage message, DateTime enqueueTimeUtc);
    Task ScheduleAsync<TMessage>(string exchange, string routingKey, TMessage message, DateTime enqueueTimeUtc);
}