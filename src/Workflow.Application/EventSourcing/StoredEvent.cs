using System.Text.Json;

namespace Workflow.Application.EventSourcing;

/// <summary>
/// Represents an event read from an aggregate stream.
/// </summary>
/// <param name="EventId">Unique id of the event.</param>
/// <param name="EventType">Logical event type name.</param>
/// <param name="OccurredUtc">When the event occurred in UTC.</param>
/// <param name="Data">JSON payload of the event.</param>
/// <param name="CorrelationId">Workflow correlation id (optional).</param>
/// <param name="CausationId">Id of the event that caused this event (optional).</param>
/// <param name="Version">Stream version (1-based) of the event.</param>
public sealed record StoredEvent(
    string EventId,
    string EventType,
    DateTimeOffset OccurredUtc,
    JsonElement Data,
    string? CorrelationId,
    string? CausationId,
    int Version);
