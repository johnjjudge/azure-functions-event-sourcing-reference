using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Workflow.Application.UseCases.Discover;

namespace Workflow.Functions.Functions;

/// <summary>
/// Timer-triggered entrypoint into the workflow.
/// </summary>
/// <remarks>
/// The schedule is configurable via the <c>DiscoverSchedule</c> app setting.
/// </remarks>
public sealed class DiscoverUnprocessedRequestsFunction
{
    private readonly DiscoverUnprocessedRequestsHandler _handler;
    private readonly ILogger<DiscoverUnprocessedRequestsFunction> _logger;

    /// <summary>
    /// Creates a new instance.
    /// </summary>
    public DiscoverUnprocessedRequestsFunction(
        DiscoverUnprocessedRequestsHandler handler,
        ILogger<DiscoverUnprocessedRequestsFunction> logger)
    {
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Runs on a timer and discovers new intake rows.
    /// </summary>
    /// <param name="timerInfo">Timer schedule metadata.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [Function(nameof(DiscoverUnprocessedRequestsFunction))]
    public async Task RunAsync(
        [TimerTrigger("%DiscoverSchedule%", RunOnStartup = false)] TimerInfo timerInfo,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Discover timer fired. ScheduleStatus={ScheduleStatus}",
            timerInfo?.ScheduleStatus);

        await _handler.ExecuteAsync(cancellationToken).ConfigureAwait(false);
    }
}
