using Azure.Messaging;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Workflow.Application.Contracts;
using Workflow.Application.Telemetry;
using Workflow.Application.UseCases.PrepareSubmission;
using Workflow.Functions.Telemetry;

namespace Workflow.Functions.Functions;

/// <summary>
/// Prepares a submission for an external service when a request is discovered.
/// </summary>
public sealed class PrepareSubmissionFunction
{
    private readonly ICorrelationContextAccessor _correlation;
    private readonly PrepareSubmissionHandler _handler;
    private readonly ILogger<PrepareSubmissionFunction> _logger;

    /// <summary>
    /// Creates a new instance.
    /// </summary>
    public PrepareSubmissionFunction(
        ICorrelationContextAccessor correlation,
        PrepareSubmissionHandler handler,
        ILogger<PrepareSubmissionFunction> logger)
    {
        _correlation = correlation ?? throw new ArgumentNullException(nameof(correlation));
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Handles the <see cref="EventTypes.RequestDiscovered"/> CloudEvent.
    /// </summary>
    [Function("PrepareSubmission")]
    public async Task Run([EventGridTrigger] CloudEvent cloudEvent, CancellationToken cancellationToken)
    {
        // If this endpoint receives other event types (e.g., a broad subscription), ignore them.
        if (!string.Equals(cloudEvent.Type, EventTypes.RequestDiscovered, StringComparison.Ordinal))
        {
            _logger.LogDebug("PrepareSubmission ignoring CloudEvent Type={Type} Id={Id}", cloudEvent.Type, cloudEvent.Id);
            return;
        }

        if (string.IsNullOrWhiteSpace(cloudEvent.Id))
        {
            _logger.LogWarning("PrepareSubmission received CloudEvent with missing Id; ignoring.");
            return;
        }

        var correlation = CloudEventHelpers.ExtractCorrelation(cloudEvent);
        _correlation.Current = correlation;

        try
        {
            var payload = CloudEventHelpers.DeserializeData<RequestDiscoveredPayload>(cloudEvent);

            await _handler.ExecuteAsync(
                new PrepareSubmissionCommand(
                    TriggeringEventId: cloudEvent.Id,
                    Discovered: payload,
                    Correlation: correlation),
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _correlation.Current = null;
        }
    }
}
