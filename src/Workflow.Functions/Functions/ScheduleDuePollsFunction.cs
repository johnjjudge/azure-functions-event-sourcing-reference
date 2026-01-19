using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Workflow.Application.UseCases.Polling;

namespace Workflow.Functions.Functions;

/// <summary>
/// Timer-triggered poll scheduler.
/// </summary>
/// <remarks>
/// <para>
/// Event Grid does not provide delayed delivery semantics. To implement "poll every X minutes",
/// the system uses a timer-triggered scheduler that queries the projection store for requests whose
/// <c>NextPollAtUtc</c> is due and emits <c>workflow.job.pollrequested.v1</c> events.
/// </para>
/// <para>
/// The schedule is configurable via the <c>PollSchedule</c> app setting.
/// </para>
/// </remarks>
public sealed class ScheduleDuePollsFunction
{
    private readonly ScheduleDuePollsHandler _handler;
    private readonly ILogger<ScheduleDuePollsFunction> _logger;

    /// <summary>
    /// Creates a new instance.
    /// </summary>
    public ScheduleDuePollsFunction(
        ScheduleDuePollsHandler handler,
        ILogger<ScheduleDuePollsFunction> logger)
    {
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Runs on a timer and schedules due poll requests.
    /// </summary>
    /// <param name="timerInfo">Timer schedule metadata.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [Function(nameof(ScheduleDuePollsFunction))]
    public async Task RunAsync(
        [TimerTrigger("%PollSchedule%", RunOnStartup = false)] TimerInfo timerInfo,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Poll scheduler timer fired. ScheduleStatus={ScheduleStatus}",
            timerInfo?.ScheduleStatus);

        await _handler.ExecuteAsync(cancellationToken).ConfigureAwait(false);
    }
}
