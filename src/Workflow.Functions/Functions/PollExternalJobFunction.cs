using Azure.Messaging;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Workflow.Application.Contracts;
using Workflow.Application.Telemetry;
using Workflow.Application.UseCases.Polling;
using Workflow.Functions.Telemetry;

namespace Workflow.Functions.Functions;

/// <summary>
/// Polls the external service for the status of a submitted job.
/// </summary>
public sealed class PollExternalJobFunction
{
    private readonly ICorrelationContextAccessor _correlation;
    private readonly PollExternalJobHandler _handler;
    private readonly ILogger<PollExternalJobFunction> _logger;

    /// <summary>
    /// Creates a new instance.
    /// </summary>
    public PollExternalJobFunction(
        ICorrelationContextAccessor correlation,
        PollExternalJobHandler handler,
        ILogger<PollExternalJobFunction> logger)
    {
        _correlation = correlation ?? throw new ArgumentNullException(nameof(correlation));
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Handles the <see cref="EventTypes.JobPollRequested"/> CloudEvent.
    /// </summary>
    [Function("PollExternalJob")]
    public async Task Run([EventGridTrigger] CloudEvent cloudEvent, CancellationToken cancellationToken)
    {
        if (!string.Equals(cloudEvent.Type, EventTypes.JobPollRequested, StringComparison.Ordinal))
        {
            _logger.LogDebug("PollExternalJob ignoring CloudEvent Type={Type} Id={Id}", cloudEvent.Type, cloudEvent.Id);
            return;
        }

        if (string.IsNullOrWhiteSpace(cloudEvent.Id))
        {
            _logger.LogWarning("PollExternalJob received CloudEvent with missing Id; ignoring.");
            return;
        }

        var correlation = CloudEventHelpers.ExtractCorrelation(cloudEvent);
        _correlation.Current = correlation;

        try
        {
            var payload = CloudEventHelpers.DeserializeData<JobPollRequestedPayload>(cloudEvent);

            await _handler.ExecuteAsync(
                new PollExternalJobCommand(
                    TriggeringEventId: cloudEvent.Id,
                    PollRequested: payload,
                    Correlation: correlation),
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _correlation.Current = null;
        }
    }
}
