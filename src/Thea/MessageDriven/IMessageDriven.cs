using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Thea.MessageDriven;

public interface IMessageDriven
{
    string HostName { get; set; }
    string DbKey { get; set; }
    void Start();
    void Shutdown();

    void Publish<TMessage>(string exchange, string routingKey, TMessage message, bool isTheaMessage = true);
    Task PublishAsync<TMessage>(string exchange, string routingKey, TMessage message, bool isTheaMessage = true);
    void Schedule<TMessage>(string exchange, string routingKey, TMessage message, DateTime enqueueTimeUtc, bool isTheaMessage = true);
    Task ScheduleAsync<TMessage>(string exchange, string routingKey, TMessage message, DateTime enqueueTimeUtc, bool isTheaMessage = true);
    TResponse Request<TRequst, TResponse>(string exchange, string routingKey, TRequst message, bool isTheaMessage = true);
    Task<TResponse> RequestAsync<TRequest, TResponse>(string exchange, string routingKey, TRequest message, bool isTheaMessage = true);
}
