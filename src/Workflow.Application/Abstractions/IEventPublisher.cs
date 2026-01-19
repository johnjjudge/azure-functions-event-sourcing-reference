namespace Workflow.Application.Abstractions;

/// <summary>
/// Publishes integration events (CloudEvents) to the event bus.
/// </summary>
public interface IEventPublisher
{
    /// <summary>
    /// Publishes a CloudEvent payload.
    /// </summary>
    Task PublishAsync<T>(string eventType, string subject, string eventId, T data, CancellationToken cancellationToken);
}
