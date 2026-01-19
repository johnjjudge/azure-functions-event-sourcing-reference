using Workflow.Application.Contracts;
using Workflow.Application.Telemetry;

namespace Workflow.Application.UseCases.Polling;

/// <summary>
/// Command to poll the external service for job status.
/// </summary>
/// <param name="TriggeringEventId">The incoming CloudEvent id.</param>
/// <param name="PollRequested">The poll request payload.</param>
/// <param name="Correlation">Ambient correlation details extracted from the CloudEvent (optional).</param>
public sealed record PollExternalJobCommand(
    string TriggeringEventId,
    JobPollRequestedPayload PollRequested,
    CorrelationContext? Correlation);
