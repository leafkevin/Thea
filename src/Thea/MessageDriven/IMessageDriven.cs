using System;
using System.Threading.Tasks;

namespace Thea.MessageDriven;

public interface IMessageDriven
{
    void Start();
    void Shutdown();

    void Publish<TMessage>(string exchange, string routingKey, TMessage message, bool isTheaMessage = true);
    Task PublishAsync<TMessage>(string exchange, string routingKey, TMessage message, bool isTheaMessage = true);
    void Schedule<TMessage>(string exchange, string routingKey, TMessage message, DateTime enqueueTimeUtc, bool isTheaMessage = true);
    Task ScheduleAsync<TMessage>(string exchange, string routingKey, TMessage message, DateTime enqueueTimeUtc, bool isTheaMessage = true);
}
