namespace Workflow.Application.EventSourcing;

/// <summary>
/// Represents an event ready to be appended to an aggregate stream.
/// </summary>
/// <param name="EventId">Unique identifier for the event.</param>
/// <param name="EventType">Logical event type name.</param>
/// <param name="OccurredUtc">When the event occurred in UTC.</param>
/// <param name="Data">Event payload.</param>
/// <param name="CorrelationId">Workflow correlation id (optional).</param>
/// <param name="CausationId">Id of the event that caused this event (optional).</param>
public sealed record EventToAppend(
    string EventId,
    string EventType,
    DateTimeOffset OccurredUtc,
    object Data,
    string? CorrelationId = null,
    string? CausationId = null);
