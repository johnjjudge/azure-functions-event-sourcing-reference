using Workflow.Application.Contracts;
using Workflow.Application.Telemetry;

namespace Workflow.Application.UseCases.PrepareSubmission;

/// <summary>
/// Command representing a request to prepare a workflow submission.
/// </summary>
/// <param name="TriggeringEventId">Unique identifier of the incoming integration event.</param>
/// <param name="Discovered">Payload describing the discovered request.</param>
/// <param name="Correlation">Correlation context extracted from the incoming event.</param>
public sealed record PrepareSubmissionCommand(
    string TriggeringEventId,
    RequestDiscoveredPayload Discovered,
    CorrelationContext? Correlation);
