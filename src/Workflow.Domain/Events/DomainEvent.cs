namespace Workflow.Domain.Events;

/// <summary>
/// Base type for domain events.
/// </summary>
public abstract record DomainEvent
{
    /// <summary>
    /// When the event occurred (UTC).
    /// </summary>
    public DateTimeOffset OccurredUtc { get; init; } = DateTimeOffset.UtcNow;
}
