namespace Workflow.Application.Abstractions;

/// <summary>
/// Stores processing markers to make event handlers safe under at-least-once delivery.
/// </summary>
/// <remarks>
/// This abstraction supports a simple leasing model:
/// <list type="number">
/// <item><description>Attempt to begin processing (creates/renews a lease).</description></item>
/// <item><description>Mark the item as completed once the handler has successfully applied side effects.</description></item>
/// </list>
/// If a handler crashes after acquiring a lease, another invocation may take over after lease expiry.
/// </remarks>
public interface IIdempotencyStore
{
    /// <summary>
    /// Attempts to begin processing an event for a handler.
    /// </summary>
    /// <param name="handlerName">A stable, human-readable handler name.</param>
    /// <param name="eventId">Unique identifier of the incoming event.</param>
    /// <param name="leaseDuration">Duration to lease processing before another invocation can take over.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// <c>true</c> if the caller owns the lease and should proceed; <c>false</c> if the event was already completed.
    /// </returns>
    Task<bool> TryBeginAsync(
        string handlerName,
        string eventId,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken);

    /// <summary>
    /// Marks a previously begun handler/event pair as completed.
    /// </summary>
    /// <param name="handlerName">Handler name used in <see cref="TryBeginAsync"/>.</param>
    /// <param name="eventId">Event id used in <see cref="TryBeginAsync"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task MarkCompletedAsync(string handlerName, string eventId, CancellationToken cancellationToken);
}
