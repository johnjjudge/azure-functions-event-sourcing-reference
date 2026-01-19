using Workflow.Application.Contracts;
using Workflow.Application.Telemetry;

namespace Workflow.Application.UseCases.Completion;

/// <summary>
/// Command for completing the workflow after a terminal status is reached.
/// </summary>
/// <param name="TriggeringEventId">The CloudEvent id that triggered this handler invocation.</param>
/// <param name="Terminal">The terminal status payload.</param>
/// <param name="Correlation">Correlation and causation identifiers propagated from the triggering CloudEvent.</param>
public sealed record CompleteRequestCommand(
    string TriggeringEventId,
    TerminalStatusReachedPayload Terminal,
    CorrelationContext? Correlation);
