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

    void Publish<TMessage>(string exchange, string routingKey, TMessage message);
    Task PublishAsync<TMessage>(string exchange, string routingKey, TMessage message);
    void Publish<TMessage>(string exchange, List<TMessage> messages, Func<TMessage, string> routingKeySelector);
    Task PublishAsync<TMessage>(string exchange, List<TMessage> messages, Func<TMessage, string> routingKeySelector);
    TResponse Request<TRequst, TResponse>(string exchange, string routingKey, TRequst message);
    Task<TResponse> RequestAsync<TRequest, TResponse>(string exchange, string routingKey, TRequest message);
    List<TResponse> Request<TRequst, TResponse>(string exchange, List<TRequst> messages, Func<TRequst, string> routingKeySelector);
    Task<List<TResponse>> RequestAsync<TRequst, TResponse>(string exchange, List<TRequst> messages, Func<TRequst, string> routingKeySelector);
}
