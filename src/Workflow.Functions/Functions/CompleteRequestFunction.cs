using Azure.Messaging;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Workflow.Application.Contracts;
using Workflow.Application.Telemetry;
using Workflow.Application.UseCases.Completion;
using Workflow.Functions.Telemetry;

namespace Workflow.Functions.Functions;

/// <summary>
/// Updates the original intake work item with the final status once the external service reaches a terminal state.
/// </summary>
public sealed class CompleteRequestFunction
{
    private readonly ICorrelationContextAccessor _correlation;
    private readonly CompleteRequestHandler _handler;
    private readonly ILogger<CompleteRequestFunction> _logger;

    /// <summary>
    /// Creates a new instance.
    /// </summary>
    public CompleteRequestFunction(
        ICorrelationContextAccessor correlation,
        CompleteRequestHandler handler,
        ILogger<CompleteRequestFunction> logger)
    {
        _correlation = correlation ?? throw new ArgumentNullException(nameof(correlation));
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Handles the <see cref="EventTypes.TerminalStatusReached"/> CloudEvent.
    /// </summary>
    [Function("CompleteRequest")]
    public async Task Run([EventGridTrigger] CloudEvent cloudEvent, CancellationToken cancellationToken)
    {
        if (!string.Equals(cloudEvent.Type, EventTypes.TerminalStatusReached, StringComparison.Ordinal))
        {
            _logger.LogDebug("CompleteRequest ignoring CloudEvent Type={Type} Id={Id}", cloudEvent.Type, cloudEvent.Id);
            return;
        }

        if (string.IsNullOrWhiteSpace(cloudEvent.Id))
        {
            _logger.LogWarning("CompleteRequest received CloudEvent with missing Id; ignoring.");
            return;
        }

        var correlation = CloudEventHelpers.ExtractCorrelation(cloudEvent);
        _correlation.Current = correlation;

        try
        {
            var payload = CloudEventHelpers.DeserializeData<TerminalStatusReachedPayload>(cloudEvent);

            await _handler.ExecuteAsync(
                new CompleteRequestCommand(
                    TriggeringEventId: cloudEvent.Id,
                    Terminal: payload,
                    Correlation: correlation),
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _correlation.Current = null;
        }
    }
}
