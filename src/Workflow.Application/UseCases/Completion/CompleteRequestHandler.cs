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

namespace Workflow.Application.UseCases.Completion;

/// <summary>
/// Handles <see cref="EventTypes.TerminalStatusReached"/> by updating the original intake row to
/// its final status and emitting <see cref="EventTypes.RequestCompleted"/>.
/// </summary>
/// <remarks>
/// <para>
/// The workflow is built for at-least-once delivery. This handler uses an idempotency store keyed by
/// the incoming CloudEvent id. If the handler crashes after appending a domain event but before publishing
/// the integration event, retries will re-publish the already-stored event.
/// </para>
/// </remarks>
public sealed class CompleteRequestHandler
{
    private const string HandlerName = "CompleteRequest";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly IIdempotencyStore _idempotency;
    private readonly IEventStore _eventStore;
    private readonly IEventPublisher _publisher;
    private readonly IEventIdFactory _eventIds;
    private readonly ITableIntakeRepository _intake;
    private readonly IClock _clock;
    private readonly ICorrelationContextAccessor _correlation;
    private readonly RequestProjectionUpdater _projectionUpdater;
    private readonly WorkflowOptions _options;
    private readonly ILogger<CompleteRequestHandler> _logger;

    /// <summary>
    /// Creates a new instance.
    /// </summary>
    public CompleteRequestHandler(
        IIdempotencyStore idempotency,
        IEventStore eventStore,
        IEventPublisher publisher,
        IEventIdFactory eventIds,
        ITableIntakeRepository intake,
        IClock clock,
        ICorrelationContextAccessor correlation,
        RequestProjectionUpdater projectionUpdater,
        IOptions<WorkflowOptions> options,
        ILogger<CompleteRequestHandler> logger)
    {
        _idempotency = idempotency ?? throw new ArgumentNullException(nameof(idempotency));
        _eventStore = eventStore ?? throw new ArgumentNullException(nameof(eventStore));
        _publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
        _eventIds = eventIds ?? throw new ArgumentNullException(nameof(eventIds));
        _intake = intake ?? throw new ArgumentNullException(nameof(intake));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _correlation = correlation ?? throw new ArgumentNullException(nameof(correlation));
        _projectionUpdater = projectionUpdater ?? throw new ArgumentNullException(nameof(projectionUpdater));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Executes the completion step.
    /// </summary>
    public async Task ExecuteAsync(CompleteRequestCommand command, CancellationToken cancellationToken)
    {
        if (command is null) throw new ArgumentNullException(nameof(command));
        if (string.IsNullOrWhiteSpace(command.TriggeringEventId))
            throw new ArgumentException("Triggering event id is required.", nameof(command));

        var requestId = RequestId.Parse(command.Terminal.RequestId);

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

            // Compute the final status based on the external service's terminal status.
            var final = command.Terminal.TerminalStatus switch
            {
                ExternalJobStatus.Pass => WorkItemStatus.Pass,
                ExternalJobStatus.Fail => WorkItemStatus.Fail,
                ExternalJobStatus.FailCanRetry => WorkItemStatus.Fail,
                _ => WorkItemStatus.Fail,
            };

            // If another worker already completed the request, re-publish.
            if (TryFindRequestCompleted(history, out var existingCompleted))
            {
                await EnsureTableIsTerminalAsync(aggregate, requestId, final, cancellationToken).ConfigureAwait(false);
                await _projectionUpdater.RebuildAndSaveAsync(requestId, cancellationToken).ConfigureAwait(false);

                await _publisher.PublishAsync(
                    eventType: EventTypes.RequestCompleted,
                    subject: Subjects.ForRequest(requestId.Value),
                    eventId: existingCompleted.EventId,
                    data: existingCompleted.Payload,
                    cancellationToken: cancellationToken).ConfigureAwait(false);

                await _idempotency.MarkCompletedAsync(HandlerName, command.TriggeringEventId, cancellationToken).ConfigureAwait(false);
                return;
            }

            // Update the intake work item.
            await EnsureTableIsTerminalAsync(aggregate, requestId, final, cancellationToken).ConfigureAwait(false);

            // Append a completion event to the event stream.
            var now = _clock.UtcNow;
            var payload = new RequestCompletedPayload(requestId.Value, final);

            var eventId = _eventIds.CreateDeterministic(
                aggregateId: requestId.Value,
                eventType: EventTypes.RequestCompleted,
                correlationId: correlationId,
                causationId: causationId,
                discriminator: $"final:{final}");

            var toAppend = new EventToAppend(
                EventId: eventId,
                EventType: EventTypes.RequestCompleted,
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
                eventType: EventTypes.RequestCompleted,
                subject: Subjects.ForRequest(requestId.Value),
                eventId: eventId,
                data: payload,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            _logger.LogInformation("Completed request {RequestId} with FinalStatus={FinalStatus}.", requestId.Value, final);

            await _idempotency.MarkCompletedAsync(HandlerName, command.TriggeringEventId, cancellationToken).ConfigureAwait(false);
        }
        catch (ConcurrencyException ex)
        {
            // Another worker advanced the stream. Treat as handled.
            _logger.LogInformation(ex, "Concurrency conflict while completing request {RequestId}.", requestId.Value);
            await _idempotency.MarkCompletedAsync(HandlerName, command.TriggeringEventId, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _correlation.Current = null;
        }
    }

    private async Task EnsureTableIsTerminalAsync(
        RequestAggregate aggregate,
        RequestId requestId,
        WorkItemStatus final,
        CancellationToken cancellationToken)
    {
        var keys = aggregate.Keys;
        if (string.IsNullOrWhiteSpace(keys.PartitionKey) || string.IsNullOrWhiteSpace(keys.RowKey))
        {
            // Fall back to parsing the RequestId format (PartitionKey|RowKey).
            var parts = requestId.Value.Split('|', 2);
            keys = new TableKeys(parts[0], parts[1]);
        }

        await _intake.MarkTerminalAsync(keys, final, cancellationToken).ConfigureAwait(false);
    }

    private static bool TryFindRequestCompleted(
        IReadOnlyList<StoredEvent> history,
        out (string EventId, RequestCompletedPayload Payload) completed)
    {
        for (var i = history.Count - 1; i >= 0; i--)
        {
            var e = history[i];
            if (!string.Equals(e.EventType, EventTypes.RequestCompleted, StringComparison.Ordinal))
            {
                continue;
            }

            var payload = JsonSerializer.Deserialize<RequestCompletedPayload>(e.Data.GetRawText(), SerializerOptions)
                         ?? throw new JsonException("Unable to deserialize RequestCompletedPayload.");

            completed = (e.EventId, payload);
            return true;
        }

        completed = default;
        return false;
    }
}
