using Workflow.Application.EventSourcing;
using Workflow.Domain.ValueObjects;

namespace Workflow.Application.Abstractions;

/// <summary>
/// Append-only store of events grouped by aggregate (event sourcing).
/// </summary>
/// <remarks>
/// Handlers must assume at-least-once delivery. Consumers should combine this store
/// with an idempotency mechanism when reacting to integration events.
/// </remarks>
public interface IEventStore
{
    /// <summary>
    /// Appends one or more events to the aggregate event stream.
    /// </summary>
    /// <param name="aggregateId">Aggregate stream identifier.</param>
    /// <param name="events">Events to append. Must be non-empty.</param>
    /// <param name="expectedVersion">
    /// Optional optimistic concurrency check. If provided, the append must only succeed
    /// if the current stream version matches.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The new stream version after the append.</returns>
    Task<int> AppendAsync(
        RequestId aggregateId,
        IReadOnlyCollection<EventToAppend> events,
        int? expectedVersion,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reads all events for the aggregate stream ordered by stream version.
    /// </summary>
    /// <param name="aggregateId">Aggregate stream identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Stream events ordered by version.</returns>
    Task<IReadOnlyList<StoredEvent>> ReadStreamAsync(RequestId aggregateId, CancellationToken cancellationToken);
}
