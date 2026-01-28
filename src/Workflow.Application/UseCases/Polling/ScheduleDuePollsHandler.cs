using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Workflow.Application.Abstractions;
using Workflow.Application.Configuration;
using Workflow.Application.Contracts;
using Workflow.Application.EventSourcing;
using Workflow.Application.Projections;
using Workflow.Application.Telemetry;

namespace Workflow.Application.UseCases.Polling;

/// <summary>
/// Queries the projection store for in-progress jobs that are due for a status poll,
/// appends a poll-request event to each request stream, and publishes a CloudEvent to Event Grid.
/// </summary>
/// <remarks>
/// <para>
/// Event Grid is an at-least-once delivery system and the poll scheduler itself is timer-triggered.
/// This handler relies on optimistic concurrency in the event store (using the projection's last
/// applied stream version as the expected version) so that only one scheduler instance can
/// advance a given request's schedule in a given interval.
/// </para>
/// <para>
/// Once the <see cref="EventTypes.JobPollRequested"/> event is appended, the projection reducer
/// moves <see cref="RequestProjection.NextPollAtUtc"/> forward by <see cref="WorkflowOptions.PollInterval"/>
/// which prevents the same request from being re-selected immediately.
/// </para>
/// </remarks>
public sealed class ScheduleDuePollsHandler
{
    private readonly IRequestProjectionRepository _projections;
    private readonly IEventStore _eventStore;
    private readonly IEventPublisher _publisher;
    private readonly IEventIdFactory _eventIds;
    private readonly IClock _clock;
    private readonly ICorrelationContextAccessor _correlation;
    private readonly RequestProjectionUpdater _projectionUpdater;
    private readonly WorkflowOptions _options;
    private readonly ILogger<ScheduleDuePollsHandler> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ScheduleDuePollsHandler"/> class.
    /// </summary>
    public ScheduleDuePollsHandler(
        IRequestProjectionRepository projections,
        IEventStore eventStore,
        IEventPublisher publisher,
        IEventIdFactory eventIds,
        IClock clock,
        ICorrelationContextAccessor correlation,
        RequestProjectionUpdater projectionUpdater,
        IOptions<WorkflowOptions> options,
        ILogger<ScheduleDuePollsHandler> logger)
    {
        _projections = projections ?? throw new ArgumentNullException(nameof(projections));
        _eventStore = eventStore ?? throw new ArgumentNullException(nameof(eventStore));
        _publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
        _eventIds = eventIds ?? throw new ArgumentNullException(nameof(eventIds));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _correlation = correlation ?? throw new ArgumentNullException(nameof(correlation));
        _projectionUpdater = projectionUpdater ?? throw new ArgumentNullException(nameof(projectionUpdater));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Executes one polling schedule pass.
    /// </summary>
    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;

        var due = await _projections.GetDueForPollAsync(
            nowUtc: now,
            take: _options.PollBatchSize,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (due.Count == 0)
        {
            _logger.LogInformation("Poll scheduler found no due requests.");
            return;
        }

        _logger.LogInformation(
            "Poll scheduler found {Count} due requests. BatchSize={BatchSize} PollIntervalMinutes={PollIntervalMinutes}",
            due.Count,
            _options.PollBatchSize,
            _options.PollInterval.TotalMinutes);

        var appended = 0;
        var published = 0;
        var skipped = 0;

        foreach (var projection in due)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (projection.ExternalJobId is null || projection.SubmitAttemptCount <= 0)
            {
                skipped++;
                continue;
            }

            var requestId = projection.RequestId;
            var correlationId = requestId.Value; // stable per workflow instance
            var attempt = projection.SubmitAttemptCount;
            var dueAt = projection.NextPollAtUtc ?? now;

            _correlation.Current = new CorrelationContext(correlationId, CausationId: null);

            try
            {
                var externalJobId = projection.ExternalJobId.Value;

                var payload = new JobPollRequestedPayload(
                    RequestId: requestId.Value,
                    ExternalJobId: externalJobId.Value,
                    Attempt: attempt);

                // Deterministic within the poll interval so retries don't create additional events.
                var discriminator = $"attempt:{attempt}|due:{dueAt.UtcDateTime:O}";

                var eventId = _eventIds.CreateDeterministic(
                    aggregateId: requestId.Value,
                    eventType: EventTypes.JobPollRequested,
                    correlationId: correlationId,
                    causationId: null,
                    discriminator: discriminator);

                var toAppend = new EventToAppend(
                    EventId: eventId,
                    EventType: EventTypes.JobPollRequested,
                    OccurredUtc: now,
                    Data: payload,
                    CorrelationId: correlationId,
                    CausationId: null);

                try
                {
                    // Use the projection's last applied version as an optimistic concurrency check.
                    // If another scheduler or handler advanced the stream, we skip.
                    await _eventStore.AppendAsync(
                        aggregateId: requestId,
                        events: new[] { toAppend },
                        expectedVersion: projection.LastAppliedEventVersion,
                        cancellationToken: cancellationToken).ConfigureAwait(false);

                    appended++;
                }
                catch (ConcurrencyException)
                {
                    // Another worker updated this stream. No need to publish another poll request.
                    skipped++;
                    continue;
                }

                // Keep the projection in sync for subsequent slices.
                await _projectionUpdater.RebuildAndSaveAsync(requestId, cancellationToken).ConfigureAwait(false);

                await _publisher.PublishAsync(
                    eventType: EventTypes.JobPollRequested,
                    subject: Subjects.ForRequest(requestId.Value),
                    eventId: eventId,
                    data: payload,
                    cancellationToken: cancellationToken).ConfigureAwait(false);

                published++;
            }
            finally
            {
                _correlation.Current = null;
            }
        }

        _logger.LogInformation(
            "Poll scheduler completed. Appended={Appended} Published={Published} Skipped={Skipped}",
            appended,
            published,
            skipped);
    }
}
