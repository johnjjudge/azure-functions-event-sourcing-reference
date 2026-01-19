namespace Workflow.Application.Telemetry;

/// <summary>
/// Captures correlation identifiers used for distributed tracing and event causality.
/// </summary>
/// <param name="CorrelationId">A stable identifier for the end-to-end workflow chain.</param>
/// <param name="CausationId">The identifier of the message/event that caused the current execution.</param>
public sealed record CorrelationContext(string CorrelationId, string CausationId);
