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
using Workflow.Domain.ValueObjects;

namespace Workflow.Application.UseCases.PrepareSubmission;

/// <summary>
/// Handles <see cref="EventTypes.RequestDiscovered"/> by preparing the first (or next) submission attempt.
/// </summary>
/// <remarks>
/// <para>
/// This handler demonstrates event sourcing: it reconstructs the request aggregate by replaying
/// the Cosmos DB event stream, then appends a <see cref="EventTypes.SubmissionPrepared"/> event.
/// </para>
/// <para>
/// Event Grid delivery is at-least-once. This handler uses <see cref="IIdempotencyStore"/> to make the
/// side effects idempotent with respect to the incoming event.
/// </para>
/// </remarks>
public sealed class PrepareSubmissionHandler
{
    private const string HandlerName = "PrepareSubmission";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly IIdempotencyStore _idempotency;
    private readonly IEventStore _eventStore;
    private readonly IEventPublisher _publisher;
    private readonly IEventIdFactory _eventIds;
    private readonly IClock _clock;
    private readonly ICorrelationContextAccessor _correlation;
    private readonly RequestProjectionUpdater _projectionUpdater;
    private readonly WorkflowOptions _options;
    private readonly ILogger<PrepareSubmissionHandler> _logger;

    /// <summary>
    /// Creates a new instance.
    /// </summary>
    public PrepareSubmissionHandler(
        IIdempotencyStore idempotency,
        IEventStore eventStore,
        IEventPublisher publisher,
        IEventIdFactory eventIds,
        IClock clock,
        ICorrelationContextAccessor correlation,
        RequestProjectionUpdater projectionUpdater,
        IOptions<WorkflowOptions> options,
        ILogger<PrepareSubmissionHandler> logger)
    {
        _idempotency = idempotency ?? throw new ArgumentNullException(nameof(idempotency));
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
    /// Processes the incoming discovery event.
    /// </summary>
    public async Task ExecuteAsync(PrepareSubmissionCommand command, CancellationToken cancellationToken)
    {
        if (command is null) throw new ArgumentNullException(nameof(command));
        if (string.IsNullOrWhiteSpace(command.TriggeringEventId)) throw new ArgumentException("Triggering event id is required.", nameof(command));

        var requestId = RequestId.Parse(command.Discovered.RequestId);

        // At-least-once delivery guard.
        var shouldProcess = await _idempotency.TryBeginAsync(
            handlerName: HandlerName,
            eventId: command.TriggeringEventId,
            leaseDuration: _options.IdempotencyLeaseDuration,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (!shouldProcess)
        {
            _logger.LogInformation(
                "{Handler} already processed event {EventId}; skipping.",
                HandlerName,
                command.TriggeringEventId);
            return;
        }

        // Establish ambient correlation for any downstream events we publish.
        var correlationId = command.Correlation?.CorrelationId ?? requestId.Value;
        var causationId = command.Correlation?.CausationId ?? command.TriggeringEventId;
        _correlation.Current = new CorrelationContext(correlationId, causationId);

        try
        {
            var history = await _eventStore.ReadStreamAsync(requestId, cancellationToken).ConfigureAwait(false);
            var aggregate = RequestAggregate.Rehydrate(requestId, history);

            if (aggregate.Status is Workflow.Domain.Model.WorkItemStatus.Pass or Workflow.Domain.Model.WorkItemStatus.Fail)
            {
                _logger.LogInformation(
                    "Request {RequestId} is already terminal ({Status}); skipping preparation.",
                    requestId.Value,
                    aggregate.Status);
                await _idempotency.MarkCompletedAsync(HandlerName, command.TriggeringEventId, cancellationToken).ConfigureAwait(false);
                return;
            }

            if (string.IsNullOrWhiteSpace(aggregate.Keys.PartitionKey) || string.IsNullOrWhiteSpace(aggregate.Keys.RowKey))
            {
                _logger.LogWarning(
                    "Request {RequestId} has no table keys in its history; cannot prepare submission.",
                    requestId.Value);
                await _idempotency.MarkCompletedAsync(HandlerName, command.TriggeringEventId, cancellationToken).ConfigureAwait(false);
                return;
            }

            // Attempt numbering is based on submissions, not preparations.
            var attempt = aggregate.SubmitAttemptCount + 1;
            if (attempt > _options.MaxSubmitAttempts)
            {
                _logger.LogInformation(
                    "Request {RequestId} has reached max attempts ({Max}); skipping preparation.",
                    requestId.Value,
                    _options.MaxSubmitAttempts);
                await _idempotency.MarkCompletedAsync(HandlerName, command.TriggeringEventId, cancellationToken).ConfigureAwait(false);
                return;
            }

            if (aggregate.HasPreparedSubmission(attempt))
            {
                _logger.LogInformation(
                    "Request {RequestId} already prepared attempt {Attempt}; re-publishing stored event.",
                    requestId.Value,
                    attempt);

                if (TryFindPreparedEvent(history, attempt, out var existing))
                {
                    _correlation.Current = new CorrelationContext(
                        existing.Event.CorrelationId ?? correlationId,
                        existing.Event.CausationId ?? causationId);

                    await _projectionUpdater.RebuildAndSaveAsync(requestId, cancellationToken).ConfigureAwait(false);

                    await _publisher.PublishAsync(
                        eventType: EventTypes.SubmissionPrepared,
                        subject: Subjects.ForRequest(requestId.Value),
                        eventId: existing.Event.EventId,
                        data: existing.Payload,
                        cancellationToken: cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    _logger.LogWarning(
                        "Stored submission-prepared event not found for {RequestId} Attempt={Attempt}.",
                        requestId.Value,
                        attempt);
                }

                await _idempotency.MarkCompletedAsync(HandlerName, command.TriggeringEventId, cancellationToken).ConfigureAwait(false);
                return;
            }

            var now = _clock.UtcNow;
            var payload = new SubmissionPreparedPayload(
                RequestId: requestId.Value,
                PartitionKey: aggregate.Keys.PartitionKey,
                RowKey: aggregate.Keys.RowKey,
                Attempt: attempt);

            var preparedEventId = _eventIds.CreateDeterministic(
                aggregateId: requestId.Value,
                eventType: EventTypes.SubmissionPrepared,
                correlationId: correlationId,
                causationId: causationId,
                discriminator: $"attempt:{attempt}");

            var toAppend = new EventToAppend(
                EventId: preparedEventId,
                EventType: EventTypes.SubmissionPrepared,
                OccurredUtc: now,
                Data: payload,
                CorrelationId: correlationId,
                CausationId: causationId);

            // Concurrency check prevents duplicate appends if another handler instance advances the stream.
            await _eventStore.AppendAsync(
                aggregateId: requestId,
                events: new[] { toAppend },
                expectedVersion: aggregate.Version,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            await _projectionUpdater.RebuildAndSaveAsync(requestId, cancellationToken).ConfigureAwait(false);

            await _publisher.PublishAsync(
                eventType: EventTypes.SubmissionPrepared,
                subject: Subjects.ForRequest(requestId.Value),
                eventId: preparedEventId,
                data: payload,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            await _idempotency.MarkCompletedAsync(HandlerName, command.TriggeringEventId, cancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "Prepared submission for {RequestId} Attempt={Attempt}",
                requestId.Value,
                attempt);
        }
        finally
        {
            _correlation.Current = null;
        }
    }

    private static bool TryFindPreparedEvent(
        IReadOnlyList<StoredEvent> history,
        int attempt,
        out (StoredEvent Event, SubmissionPreparedPayload Payload) prepared)
    {
        foreach (var e in history)
        {
            if (!string.Equals(e.EventType, EventTypes.SubmissionPrepared, StringComparison.Ordinal))
            {
                continue;
            }

            var payload = e.Data.Deserialize<SubmissionPreparedPayload>(SerializerOptions);
            if (payload is not null && payload.Attempt == attempt)
            {
                prepared = (e, payload);
                return true;
            }
        }

        prepared = default;
        return false;
    }
}
