namespace Workflow.Application.Abstractions;

/// <summary>
/// Generates event identifiers.
/// </summary>
/// <remarks>
/// In event-driven systems, events may be retried and delivered at-least-once. To support safe
/// idempotency, handlers should prefer deterministic ids derived from stable inputs (e.g., aggregate id,
/// event type, and a causation id).
/// </remarks>
public interface IEventIdFactory
{
    /// <summary>
    /// Creates a deterministic event id based on stable input values.
    /// </summary>
    /// <param name="aggregateId">Aggregate identifier the event belongs to.</param>
    /// <param name="eventType">Logical event type (e.g., <c>workflow.request.discovered.v1</c>).</param>
    /// <param name="correlationId">Correlation id for the workflow (optional but recommended).</param>
    /// <param name="causationId">Id of the event that caused this event (optional but recommended).</param>
    /// <param name="discriminator">Optional extra discriminator (e.g., attempt number) to avoid collisions.</param>
    /// <returns>A URL-safe deterministic identifier string.</returns>
    string CreateDeterministic(string aggregateId, string eventType, string? correlationId, string? causationId, string? discriminator = null);
}
