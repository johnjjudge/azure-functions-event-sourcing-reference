using Azure.Messaging;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Workflow.Application.Contracts;
using Workflow.Application.Telemetry;
using Workflow.Application.UseCases.SubmitJob;
using Workflow.Functions.Telemetry;

namespace Workflow.Functions.Functions;

/// <summary>
/// Submits a prepared request to the external service.
/// </summary>
public sealed class SubmitJobFunction
{
    private readonly ICorrelationContextAccessor _correlation;
    private readonly SubmitJobHandler _handler;
    private readonly ILogger<SubmitJobFunction> _logger;

    /// <summary>
    /// Creates a new instance.
    /// </summary>
    public SubmitJobFunction(
        ICorrelationContextAccessor correlation,
        SubmitJobHandler handler,
        ILogger<SubmitJobFunction> logger)
    {
        _correlation = correlation ?? throw new ArgumentNullException(nameof(correlation));
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Handles the <see cref="EventTypes.SubmissionPrepared"/> CloudEvent.
    /// </summary>
    [Function("SubmitJob")]
    public async Task Run([EventGridTrigger] CloudEvent cloudEvent, CancellationToken cancellationToken)
    {
        // If this endpoint receives other event types (e.g., a broad subscription), ignore them.
        if (!string.Equals(cloudEvent.Type, EventTypes.SubmissionPrepared, StringComparison.Ordinal))
        {
            _logger.LogDebug("SubmitJob ignoring CloudEvent Type={Type} Id={Id}", cloudEvent.Type, cloudEvent.Id);
            return;
        }

        if (string.IsNullOrWhiteSpace(cloudEvent.Id))
        {
            _logger.LogWarning("SubmitJob received CloudEvent with missing Id; ignoring.");
            return;
        }

        var correlation = CloudEventHelpers.ExtractCorrelation(cloudEvent);
        _correlation.Current = correlation;

        try
        {
            var payload = CloudEventHelpers.DeserializeData<SubmissionPreparedPayload>(cloudEvent);

            await _handler.ExecuteAsync(
                new SubmitJobCommand(
                    TriggeringEventId: cloudEvent.Id,
                    Prepared: payload,
                    Correlation: correlation),
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _correlation.Current = null;
        }
    }
}
