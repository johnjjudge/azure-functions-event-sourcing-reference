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

namespace Workflow.Application.UseCases.SubmitJob;

/// <summary>
/// Handles <see cref="EventTypes.SubmissionPrepared"/> by submitting a job to the external service.
/// </summary>
/// <remarks>
/// <para>
/// This handler performs a real side effect (calling the external service) and then records the result
/// as an immutable event in the event store. Downstream steps react to the emitted integration event.
/// </para>
/// <para>
/// Event Grid delivery is at-least-once. This handler uses <see cref="IIdempotencyStore"/> to ensure the
/// incoming prepared-submission event is processed exactly once with respect to side effects.
/// </para>
/// <para>
/// If a prior invocation appended the <see cref="EventTypes.JobSubmitted"/> event but crashed before
/// publishing the integration event, this handler will re-publish it.
/// </para>
/// </remarks>
public sealed class SubmitJobHandler
{
    private const string HandlerName = "SubmitJob";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly IIdempotencyStore _idempotency;
    private readonly IEventStore _eventStore;
    private readonly IExternalServiceClient _external;
    private readonly IEventPublisher _publisher;
    private readonly IEventIdFactory _eventIds;
    private readonly IClock _clock;
    private readonly ICorrelationContextAccessor _correlation;
    private readonly RequestProjectionUpdater _projectionUpdater;
    private readonly WorkflowOptions _options;
    private readonly ILogger<SubmitJobHandler> _logger;

    /// <summary>
    /// Creates a new instance.
    /// </summary>
    public SubmitJobHandler(
        IIdempotencyStore idempotency,
        IEventStore eventStore,
        IExternalServiceClient external,
        IEventPublisher publisher,
        IEventIdFactory eventIds,
        IClock clock,
        ICorrelationContextAccessor correlation,
        RequestProjectionUpdater projectionUpdater,
        IOptions<WorkflowOptions> options,
        ILogger<SubmitJobHandler> logger)
    {
        _idempotency = idempotency ?? throw new ArgumentNullException(nameof(idempotency));
        _eventStore = eventStore ?? throw new ArgumentNullException(nameof(eventStore));
        _external = external ?? throw new ArgumentNullException(nameof(external));
        _publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
        _eventIds = eventIds ?? throw new ArgumentNullException(nameof(eventIds));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _correlation = correlation ?? throw new ArgumentNullException(nameof(correlation));
        _projectionUpdater = projectionUpdater ?? throw new ArgumentNullException(nameof(projectionUpdater));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Processes the incoming prepared-submission event.
    /// </summary>
    public async Task ExecuteAsync(SubmitJobCommand command, CancellationToken cancellationToken)
    {
        if (command is null) throw new ArgumentNullException(nameof(command));
        if (string.IsNullOrWhiteSpace(command.TriggeringEventId)) throw new ArgumentException("Triggering event id is required.", nameof(command));

        var requestId = RequestId.Parse(command.Prepared.RequestId);

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
                    "Request {RequestId} is already terminal ({Status}); skipping submit.",
                    requestId.Value,
                    aggregate.Status);
                await _idempotency.MarkCompletedAsync(HandlerName, command.TriggeringEventId, cancellationToken).ConfigureAwait(false);
                return;
            }

            var attempt = command.Prepared.Attempt;
            if (attempt <= 0)
            {
                _logger.LogWarning("Prepared payload contained invalid Attempt={Attempt} for {RequestId}", attempt, requestId.Value);
                await _idempotency.MarkCompletedAsync(HandlerName, command.TriggeringEventId, cancellationToken).ConfigureAwait(false);
                return;
            }

            if (attempt > _options.MaxSubmitAttempts)
            {
                _logger.LogInformation(
                    "Request {RequestId} attempt {Attempt} exceeds max attempts ({Max}); skipping submit.",
                    requestId.Value,
                    attempt,
                    _options.MaxSubmitAttempts);
                await _idempotency.MarkCompletedAsync(HandlerName, command.TriggeringEventId, cancellationToken).ConfigureAwait(false);
                return;
            }

            if (string.IsNullOrWhiteSpace(aggregate.Keys.PartitionKey) || string.IsNullOrWhiteSpace(aggregate.Keys.RowKey))
            {
                _logger.LogWarning(
                    "Request {RequestId} has no table keys in its history; cannot submit.",
                    requestId.Value);
                await _idempotency.MarkCompletedAsync(HandlerName, command.TriggeringEventId, cancellationToken).ConfigureAwait(false);
                return;
            }

            // If we already have a JobSubmitted event for this attempt, re-publish the integration event.
            if (aggregate.HasSubmittedJob(attempt))
            {
                var existing = history
                    .Where(e => string.Equals(e.EventType, EventTypes.JobSubmitted, StringComparison.Ordinal))
                    .Select(e => (Event: e, Payload: e.Data.Deserialize<JobSubmittedPayload>(SerializerOptions)))
                    .Where(x => x.Payload is not null && x.Payload.Attempt == attempt)
                    .Select(x => (x.Event, Payload: x.Payload!))
                    .OrderByDescending(x => x.Event.Version)
                    .FirstOrDefault();

                if (existing.Payload is not null)
                {
                    _logger.LogInformation(
                        "Job already submitted for {RequestId} Attempt={Attempt}; re-publishing JobSubmitted event.",
                        requestId.Value,
                        attempt);

                    _correlation.Current = new CorrelationContext(existing.Event.CorrelationId ?? correlationId, existing.Event.CausationId ?? causationId);

                    await _publisher.PublishAsync(
                        eventType: EventTypes.JobSubmitted,
                        subject: Subjects.ForRequest(requestId.Value),
                        eventId: existing.Event.EventId,
                        data: existing.Payload,
                        cancellationToken: cancellationToken).ConfigureAwait(false);

                    // Ensure the projection reflects the stored events even if a previous invocation crashed.
                    await _projectionUpdater.RebuildAndSaveAsync(requestId, cancellationToken).ConfigureAwait(false);

                    await _idempotency.MarkCompletedAsync(HandlerName, command.TriggeringEventId, cancellationToken).ConfigureAwait(false);
                    return;
                }
            }

            // Side effect: create a job in the external service.
            // The reference mock service is deterministic/idempotent for (RequestId, Attempt).
            var (jobId, initialStatus) = await _external.CreateJobAsync(requestId, attempt, cancellationToken).ConfigureAwait(false);

            var now = _clock.UtcNow;
            var payload = new JobSubmittedPayload(
                RequestId: requestId.Value,
                PartitionKey: aggregate.Keys.PartitionKey,
                RowKey: aggregate.Keys.RowKey,
                ExternalJobId: jobId.Value,
                Attempt: attempt);

            var submittedEventId = _eventIds.CreateDeterministic(
                aggregateId: requestId.Value,
                eventType: EventTypes.JobSubmitted,
                correlationId: correlationId,
                causationId: causationId,
                discriminator: $"attempt:{attempt}");

            var toAppend = new EventToAppend(
                EventId: submittedEventId,
                EventType: EventTypes.JobSubmitted,
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
                eventType: EventTypes.JobSubmitted,
                subject: Subjects.ForRequest(requestId.Value),
                eventId: submittedEventId,
                data: payload,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            await _idempotency.MarkCompletedAsync(HandlerName, command.TriggeringEventId, cancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "Submitted external job for {RequestId} Attempt={Attempt} JobId={JobId} InitialStatus={Status}",
                requestId.Value,
                attempt,
                jobId.Value,
                initialStatus);
        }
        finally
        {
            _correlation.Current = null;
        }
    }
}
