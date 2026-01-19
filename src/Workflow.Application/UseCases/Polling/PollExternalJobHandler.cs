using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Workflow.Application.Abstractions;
using Workflow.Application.Configuration;
using Workflow.Application.Contracts;
using Workflow.Application.Domain;
using Workflow.Application.EventSourcing;
using Workflow.Application.Projections;
using Workflow.Application.Telemetry;
using Workflow.Domain.Model;
using Workflow.Domain.ValueObjects;

namespace Workflow.Application.UseCases.Polling;

/// <summary>
/// Handles <see cref="EventTypes.JobPollRequested"/> by polling the external service for job status
/// and emitting either a resubmission request (via <see cref="EventTypes.SubmissionPrepared"/>)
/// or a terminal status event (via <see cref="EventTypes.TerminalStatusReached"/>).
/// </summary>
/// <remarks>
/// <para>
/// The workflow is designed for at-least-once delivery. This handler uses an idempotency store
/// keyed by the incoming CloudEvent id. If the handler crashes after appending a domain event
/// but before publishing the integration event, retries will re-publish the already-stored event.
/// </para>
/// <para>
/// The poll scheduler (slice 10) advances the projection's next-due time as soon as it appends
/// <see cref="EventTypes.JobPollRequested"/>. Therefore, this handler only appends additional events
/// when a retry or terminal transition occurs.
/// </para>
/// </remarks>
public sealed class PollExternalJobHandler
{
    private const string HandlerName = "PollExternalJob";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly IIdempotencyStore _idempotency;
    private readonly IEventStore _eventStore;
    private readonly IEventPublisher _publisher;
    private readonly IEventIdFactory _eventIds;
    private readonly IExternalServiceClient _external;
    private readonly IClock _clock;
    private readonly ICorrelationContextAccessor _correlation;
    private readonly RequestProjectionUpdater _projectionUpdater;
    private readonly WorkflowOptions _options;
    private readonly ILogger<PollExternalJobHandler> _logger;

    /// <summary>
    /// Creates a new instance.
    /// </summary>
    public PollExternalJobHandler(
        IIdempotencyStore idempotency,
        IEventStore eventStore,
        IEventPublisher publisher,
        IEventIdFactory eventIds,
        IExternalServiceClient external,
        IClock clock,
        ICorrelationContextAccessor correlation,
        RequestProjectionUpdater projectionUpdater,
        IOptions<WorkflowOptions> options,
        ILogger<PollExternalJobHandler> logger)
    {
        _idempotency = idempotency ?? throw new ArgumentNullException(nameof(idempotency));
        _eventStore = eventStore ?? throw new ArgumentNullException(nameof(eventStore));
        _publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
        _eventIds = eventIds ?? throw new ArgumentNullException(nameof(eventIds));
        _external = external ?? throw new ArgumentNullException(nameof(external));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _correlation = correlation ?? throw new ArgumentNullException(nameof(correlation));
        _projectionUpdater = projectionUpdater ?? throw new ArgumentNullException(nameof(projectionUpdater));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Executes the poll operation.
    /// </summary>
    public async Task ExecuteAsync(PollExternalJobCommand command, CancellationToken cancellationToken)
    {
        if (command is null) throw new ArgumentNullException(nameof(command));
        if (string.IsNullOrWhiteSpace(command.TriggeringEventId))
            throw new ArgumentException("Triggering event id is required.", nameof(command));

        var requestId = RequestId.Parse(command.PollRequested.RequestId);

        var shouldProcess = await _idempotency.TryBeginAsync(
            handlerName: HandlerName,
            eventId: command.TriggeringEventId,
            leaseDuration: _options.IdempotencyLeaseDuration,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (!shouldProcess)
        {
            _logger.LogInformation("{Handler} already processed event {EventId}; skipping.", HandlerName, command.TriggeringEventId);
            return;
        }

        var correlationId = command.Correlation?.CorrelationId ?? requestId.Value;
        var causationId = command.Correlation?.CausationId ?? command.TriggeringEventId;
        _correlation.Current = new CorrelationContext(correlationId, causationId);

        try
        {
            var history = await _eventStore.ReadStreamAsync(requestId, cancellationToken).ConfigureAwait(false);
            var aggregate = RequestAggregate.Rehydrate(requestId, history);

            if (aggregate.Status is WorkItemStatus.Pass or WorkItemStatus.Fail)
            {
                _logger.LogInformation("Request {RequestId} is already terminal ({Status}); ignoring poll.", requestId.Value, aggregate.Status);
                await _idempotency.MarkCompletedAsync(HandlerName, command.TriggeringEventId, cancellationToken).ConfigureAwait(false);
                return;
            }

            var jobId = new ExternalJobId(command.PollRequested.ExternalJobId);

            // If another worker already recorded a terminal status for this attempt, re-publish it.
            if (TryFindTerminalEvent(history, out var existingTerminal))
            {
                await _projectionUpdater.RebuildAndSaveAsync(requestId, cancellationToken).ConfigureAwait(false);
                await _publisher.PublishAsync(
                    eventType: EventTypes.TerminalStatusReached,
                    subject: Subjects.ForRequest(requestId.Value),
                    eventId: existingTerminal.EventId,
                    data: existingTerminal.Payload,
                    cancellationToken: cancellationToken).ConfigureAwait(false);

                await _idempotency.MarkCompletedAsync(HandlerName, command.TriggeringEventId, cancellationToken).ConfigureAwait(false);
                return;
            }

            var status = await _external.GetStatusAsync(jobId, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Polled external job {JobId} for {RequestId}. Status={Status}", jobId.Value, requestId.Value, status);

            switch (status)
            {
                case ExternalJobStatus.Created:
                case ExternalJobStatus.Inprogress:
                    // No domain transition needed. The next poll time was already advanced by the poll scheduler.
                    await _idempotency.MarkCompletedAsync(HandlerName, command.TriggeringEventId, cancellationToken).ConfigureAwait(false);
                    return;

                case ExternalJobStatus.Pass:
                case ExternalJobStatus.Fail:
                    await RecordAndPublishTerminalAsync(requestId, aggregate, jobId, status, correlationId, causationId, cancellationToken)
                        .ConfigureAwait(false);
                    await _idempotency.MarkCompletedAsync(HandlerName, command.TriggeringEventId, cancellationToken).ConfigureAwait(false);
                    return;

                case ExternalJobStatus.FailCanRetry:
                    await HandleRetryAsync(requestId, aggregate, jobId, correlationId, causationId, cancellationToken)
                        .ConfigureAwait(false);
                    await _idempotency.MarkCompletedAsync(HandlerName, command.TriggeringEventId, cancellationToken).ConfigureAwait(false);
                    return;
                default:
                    _logger.LogWarning("Unknown external job status {Status} for {RequestId}; treating as terminal fail.", status, requestId.Value);
                    await RecordAndPublishTerminalAsync(requestId, aggregate, jobId, ExternalJobStatus.Fail, correlationId, causationId, cancellationToken)
                        .ConfigureAwait(false);
                    await _idempotency.MarkCompletedAsync(HandlerName, command.TriggeringEventId, cancellationToken).ConfigureAwait(false);
                    return;
            }
        }
        catch (ConcurrencyException ex)
        {
            // Another worker advanced the stream. We treat the poll as handled.
            _logger.LogInformation(ex, "Concurrency conflict while processing poll for {RequestId}.", requestId.Value);
            await _idempotency.MarkCompletedAsync(HandlerName, command.TriggeringEventId, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _correlation.Current = null;
        }
    }

    private async Task HandleRetryAsync(
        RequestId requestId,
        RequestAggregate aggregate,
        ExternalJobId jobId,
        string correlationId,
        string causationId,
        CancellationToken cancellationToken)
    {
        var nextAttempt = aggregate.SubmitAttemptCount + 1;
        if (nextAttempt > _options.MaxSubmitAttempts)
        {
            _logger.LogInformation(
                "Request {RequestId} reached max attempts ({Max}); treating FailCanRetry as terminal Fail.",
                requestId.Value,
                _options.MaxSubmitAttempts);

            await RecordAndPublishTerminalAsync(requestId, aggregate, jobId, ExternalJobStatus.Fail, correlationId, causationId, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        if (string.IsNullOrWhiteSpace(aggregate.Keys.PartitionKey) || string.IsNullOrWhiteSpace(aggregate.Keys.RowKey))
        {
            _logger.LogWarning("Request {RequestId} lacks table keys; cannot resubmit. Marking terminal Fail.", requestId.Value);
            await RecordAndPublishTerminalAsync(requestId, aggregate, jobId, ExternalJobStatus.Fail, correlationId, causationId, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        // If another worker already prepared this attempt, re-publish.
        var history = await _eventStore.ReadStreamAsync(requestId, cancellationToken).ConfigureAwait(false);
        if (TryFindSubmissionPrepared(history, nextAttempt, out var existingPrepared))
        {
            await _projectionUpdater.RebuildAndSaveAsync(requestId, cancellationToken).ConfigureAwait(false);
            await _publisher.PublishAsync(
                eventType: EventTypes.SubmissionPrepared,
                subject: Subjects.ForRequest(requestId.Value),
                eventId: existingPrepared.EventId,
                data: existingPrepared.Payload,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            return;
        }

        var now = _clock.UtcNow;
        var payload = new SubmissionPreparedPayload(
            RequestId: requestId.Value,
            PartitionKey: aggregate.Keys.PartitionKey,
            RowKey: aggregate.Keys.RowKey,
            Attempt: nextAttempt);

        var eventId = _eventIds.CreateDeterministic(
            aggregateId: requestId.Value,
            eventType: EventTypes.SubmissionPrepared,
            correlationId: correlationId,
            causationId: causationId,
            discriminator: $"attempt:{nextAttempt}");

        var toAppend = new EventToAppend(
            EventId: eventId,
            EventType: EventTypes.SubmissionPrepared,
            OccurredUtc: now,
            Data: payload,
            CorrelationId: correlationId,
            CausationId: causationId);

        await _eventStore.AppendAsync(
            aggregateId: requestId,
            events: new[] { toAppend },
            expectedVersion: aggregate.Version,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        await _projectionUpdater.RebuildAndSaveAsync(requestId, cancellationToken).ConfigureAwait(false);

        await _publisher.PublishAsync(
            eventType: EventTypes.SubmissionPrepared,
            subject: Subjects.ForRequest(requestId.Value),
            eventId: eventId,
            data: payload,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Requested resubmission for {RequestId}. NextAttempt={Attempt}", requestId.Value, nextAttempt);
    }

    private async Task RecordAndPublishTerminalAsync(
        RequestId requestId,
        RequestAggregate aggregate,
        ExternalJobId jobId,
        ExternalJobStatus terminal,
        string correlationId,
        string causationId,
        CancellationToken cancellationToken)
    {
        // Re-publish if already recorded.
        var history = await _eventStore.ReadStreamAsync(requestId, cancellationToken).ConfigureAwait(false);
        if (TryFindTerminalEvent(history, out var existing))
        {
            await _projectionUpdater.RebuildAndSaveAsync(requestId, cancellationToken).ConfigureAwait(false);
            await _publisher.PublishAsync(
                eventType: EventTypes.TerminalStatusReached,
                subject: Subjects.ForRequest(requestId.Value),
                eventId: existing.EventId,
                data: existing.Payload,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            return;
        }

        var attempt = Math.Max(aggregate.SubmitAttemptCount, 1);
        var now = _clock.UtcNow;

        var payload = new TerminalStatusReachedPayload(
            RequestId: requestId.Value,
            ExternalJobId: jobId.Value,
            TerminalStatus: terminal,
            Attempt: attempt);

        var eventId = _eventIds.CreateDeterministic(
            aggregateId: requestId.Value,
            eventType: EventTypes.TerminalStatusReached,
            correlationId: correlationId,
            causationId: causationId,
            discriminator: $"attempt:{attempt}|job:{jobId.Value}|status:{terminal}");

        var toAppend = new EventToAppend(
            EventId: eventId,
            EventType: EventTypes.TerminalStatusReached,
            OccurredUtc: now,
            Data: payload,
            CorrelationId: correlationId,
            CausationId: causationId);

        await _eventStore.AppendAsync(
            aggregateId: requestId,
            events: new[] { toAppend },
            expectedVersion: aggregate.Version,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        await _projectionUpdater.RebuildAndSaveAsync(requestId, cancellationToken).ConfigureAwait(false);

        await _publisher.PublishAsync(
            eventType: EventTypes.TerminalStatusReached,
            subject: Subjects.ForRequest(requestId.Value),
            eventId: eventId,
            data: payload,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Recorded terminal status for {RequestId}. Status={Status}", requestId.Value, terminal);
    }

    private static bool TryFindSubmissionPrepared(
        IReadOnlyList<StoredEvent> history,
        int attempt,
        out (string EventId, SubmissionPreparedPayload Payload) match)
    {
        foreach (var e in history)
        {
            if (!string.Equals(e.EventType, EventTypes.SubmissionPrepared, StringComparison.Ordinal))
                continue;

            var payload = e.Data.Deserialize<SubmissionPreparedPayload>(SerializerOptions);
            if (payload is not null && payload.Attempt == attempt)
            {
                match = (e.EventId, payload);
                return true;
            }
        }

        match = default;
        return false;
    }

    private static bool TryFindTerminalEvent(IReadOnlyList<StoredEvent> history, out (string EventId, TerminalStatusReachedPayload Payload) match)
    {
        foreach (var e in history)
        {
            if (!string.Equals(e.EventType, EventTypes.TerminalStatusReached, StringComparison.Ordinal))
                continue;

            var payload = e.Data.Deserialize<TerminalStatusReachedPayload>(SerializerOptions);
            if (payload is not null)
            {
                match = (e.EventId, payload);
                return true;
            }
        }

        match = default;
        return false;
    }
}
