using Azure.Messaging;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Workflow.Application.Telemetry;
using Workflow.Functions.Telemetry;

namespace Workflow.Functions.Functions;

/// <summary>
/// A minimal Event Grid-triggered function used to validate end-to-end Event Grid plumbing.
/// </summary>
/// <remarks>
/// In later slices this will be replaced by handlers for concrete event types.
/// For now, we simply log the event envelope and populate correlation context.
/// </remarks>
public sealed class EventGridSinkFunction
{
    private readonly ICorrelationContextAccessor _correlation;
    private readonly ILogger<EventGridSinkFunction> _logger;

    /// <summary>
    /// Creates a new instance.
    /// </summary>
    public EventGridSinkFunction(ICorrelationContextAccessor correlation, ILogger<EventGridSinkFunction> logger)
    {
        _correlation = correlation;
        _logger = logger;
    }

    /// <summary>
    /// Receives CloudEvents from Event Grid.
    /// </summary>
    /// <param name="cloudEvent">Incoming CloudEvent.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [Function("EventGridSink")]
    public Task Run([EventGridTrigger] CloudEvent cloudEvent, CancellationToken cancellationToken)
    {
        _correlation.Current = CloudEventHelpers.ExtractCorrelation(cloudEvent);

        _logger.LogInformation(
            "Received CloudEvent Type={Type} Id={Id} Subject={Subject} Source={Source}",
            cloudEvent.Type,
            cloudEvent.Id,
            cloudEvent.Subject,
            cloudEvent.Source);

        // Nothing else to do for the plumbing slice.
        return Task.CompletedTask;
    }
}
