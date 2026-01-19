using Workflow.Application.Contracts;
using Workflow.Application.Telemetry;

namespace Workflow.Application.UseCases.SubmitJob;

/// <summary>
/// Command representing a request to submit a prepared workflow request to the external service.
/// </summary>
/// <param name="TriggeringEventId">Unique identifier of the incoming integration event.</param>
/// <param name="Prepared">Payload describing the prepared submission.</param>
/// <param name="Correlation">Correlation context extracted from the incoming event.</param>
public sealed record SubmitJobCommand(
    string TriggeringEventId,
    SubmissionPreparedPayload Prepared,
    CorrelationContext? Correlation);
