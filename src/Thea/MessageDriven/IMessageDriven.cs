using System;
using System.Threading.Tasks;

namespace Thea.MessageDriven;

public interface IMessageDriven
{
    void Start();
    void Shutdown();

    void Publish<TMessage>(string exchange, string routingKey, TMessage message);
    Task PublishAsync<TMessage>(string exchange, string routingKey, TMessage message);
    string Request<TMessage>(string exchange, string routingKey, TMessage message);
    Task<string> RequestAsync<TMessage>(string exchange, string routingKey, TMessage message);
    void Schedule<TMessage>(string exchange, string routingKey, TMessage message, DateTime enqueueTimeUtc);
    Task ScheduleAsync<TMessage>(string exchange, string routingKey, TMessage message, DateTime enqueueTimeUtc);
}
