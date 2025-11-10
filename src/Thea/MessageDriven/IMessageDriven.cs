using System;
using System.Threading;
using System.Threading.Tasks;

namespace Thea.MessageDriven;

public interface IMessageDriven
{
    string ServiceId { get; }
    void Start();
    void Shutdown();

    Task ChangeQueue(string queueId, int workloadTotal);
    Task RemoveQueue(string queueId, int minIndex, int maxIndex);

    Task PublishAsync<TMessage>(string exchange, string routingKey, TMessage message, CancellationToken cancellationToken = default);
    Task PublishRpcAsync<TRequest>(string replyToQueue, string messageId, string exchange, string routingKey, TRequest request, CancellationToken cancellationToken = default);
    Task<TResponse> RequestAsync<TRequest, TResponse>(string exchange, string routingKey, TRequest request, int timeoutSeconds = 30, CancellationToken cancellationToken = default);
    Task ScheduleAsync<TMessage>(string exchange, string routingKey, TMessage message, DateTime enqueueTimeUtc, CancellationToken cancellationToken = default);
}