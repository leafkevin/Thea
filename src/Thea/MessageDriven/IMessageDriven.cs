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
    void Publish<TMessage>(string exchange, List<TMessage> messages, Func<TMessage, string> routingKeySelector, bool isTheaMessage = true);
    Task PublishAsync<TMessage>(string exchange, List<TMessage> messages, Func<TMessage, string> routingKeySelector, bool isTheaMessage = true);
    TResponse Request<TRequst, TResponse>(string exchange, string routingKey, TRequst message, bool isTheaMessage = true);
    Task<TResponse> RequestAsync<TRequest, TResponse>(string exchange, string routingKey, TRequest message, bool isTheaMessage = true);
    List<TResponse> Request<TRequst, TResponse>(string exchange, List<TRequst> messages, Func<TRequst, string> routingKeySelector, bool isTheaMessage = true);
    Task<List<TResponse>> RequestAsync<TRequst, TResponse>(string exchange, List<TRequst> messages, Func<TRequst, string> routingKeySelector, bool isTheaMessage = true);
}
