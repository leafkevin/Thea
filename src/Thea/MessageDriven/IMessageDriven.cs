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

    TheaResponse Request<TMessage>(string exchange, string routingKey, TMessage message);
    Task<TheaResponse> RequestAsync<TMessage>(string exchange, string routingKey, TMessage message);
    void Publish<TMessage>(string exchange, string routingKey, TMessage message);
    Task PublishAsync<TMessage>(string exchange, string routingKey, TMessage message);
    Task<TheaResponse[]> RequestAsync<TMessage>(string exchange, List<TMessage> messages, Func<TMessage, string> groupRoutingKeySelector);
    TheaResponse[] Request<TMessage>(string exchange, List<TMessage> messages, Func<TMessage, string> groupRoutingKeySelector);
}
