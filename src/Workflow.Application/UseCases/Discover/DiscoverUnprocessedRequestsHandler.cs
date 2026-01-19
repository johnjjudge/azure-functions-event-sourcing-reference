using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Workflow.Application.Abstractions;
using Workflow.Application.Configuration;
using Workflow.Application.Contracts;
using Workflow.Application.EventSourcing;
using Workflow.Application.Projections;
using Workflow.Application.Telemetry;
using Workflow.Domain.ValueObjects;

namespace Workflow.Application.UseCases.Discover;

/// <summary>
/// Discovers unprocessed intake rows, claims them with a lease, appends a discovery event to the event store,
/// and publishes an integration event to Event Grid.
/// </summary>
/// <remarks>
/// <para>
/// This is the entrypoint into the workflow. It is timer-triggered and therefore must be tolerant to
/// repeated invocations and partial failures.
/// </para>
/// <para>
/// The repository claim is an optimization to reduce duplicates. Correctness is driven by the event store:
/// the discovery event is appended with <c>expectedVersion = 0</c>, which makes it idempotent.
/// </para>
/// </remarks>
public sealed class DiscoverUnprocessedRequestsHandler
{
    private readonly ITableIntakeRepository _intake;
    private readonly IEventStore _eventStore;
    private readonly IEventPublisher _publisher;
    private readonly IEventIdFactory _eventIds;
    private readonly IClock _clock;
    private readonly ICorrelationContextAccessor _correlation;
    private readonly RequestProjectionUpdater _projectionUpdater;
    private readonly WorkflowOptions _options;
    private readonly ILogger<DiscoverUnprocessedRequestsHandler> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="DiscoverUnprocessedRequestsHandler"/> class.
    /// </summary>
    public DiscoverUnprocessedRequestsHandler(
        ITableIntakeRepository intake,
        IEventStore eventStore,
        IEventPublisher publisher,
        IEventIdFactory eventIds,
        IClock clock,
        ICorrelationContextAccessor correlation,
        RequestProjectionUpdater projectionUpdater,
        IOptions<WorkflowOptions> options,
        ILogger<DiscoverUnprocessedRequestsHandler> logger)
    {
        _intake = intake ?? throw new ArgumentNullException(nameof(intake));
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
    /// Executes the discovery pass.
    /// </summary>
    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var leaseUntil = now + _options.LeaseDuration;

        var candidates = await _intake.GetAvailableUnprocessedAsync(
            take: _options.IntakeBatchSize,
            nowUtc: now,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (candidates.Count == 0)
        {
            _logger.LogInformation("Discovery found no eligible rows.");
            return;
        }

        _logger.LogInformation(
            "Discovery found {Count} candidate rows. Lease={LeaseMinutes}m BatchSize={BatchSize}",
            candidates.Count,
            _options.LeaseDuration.TotalMinutes,
            _options.IntakeBatchSize);

        var claimed = 0;
        var published = 0;

        foreach (var row in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Claim the row first (best-effort). If another worker claimed it, skip.
            if (!await _intake.TryClaimAsync(row, leaseUntil, cancellationToken).ConfigureAwait(false))
            {
                continue;
            }

            claimed++;

            var requestId = RequestId.FromTableKeys(row.Keys.PartitionKey, row.Keys.RowKey);
            var correlationId = requestId.Value; // stable per workflow instance

            // Establish ambient correlation so the event publisher stamps CloudEvent extensions.
            _correlation.Current = new CorrelationContext(correlationId, causationId: null);

            try
            {
                var payload = new RequestDiscoveredPayload(
                    RequestId: requestId.Value,
                    PartitionKey: row.Keys.PartitionKey,
                    RowKey: row.Keys.RowKey);

                var eventId = _eventIds.CreateDeterministic(
                    aggregateId: requestId.Value,
                    eventType: EventTypes.RequestDiscovered,
                    correlationId: correlationId,
                    causationId: null);

                var toAppend = new EventToAppend(
                    EventId: eventId,
                    EventType: EventTypes.RequestDiscovered,
                    OccurredUtc: now,
                    Data: payload,
                    CorrelationId: correlationId,
                    CausationId: null);

                // Idempotency guard: discovery is always the first event. If the stream already exists,
                // this indicates the row has already been introduced into the workflow.
                try
                {
                    await _eventStore.AppendAsync(
                        aggregateId: requestId,
                        events: new[] { toAppend },
                        expectedVersion: 0,
                        cancellationToken: cancellationToken).ConfigureAwait(false);
                }
                catch (ConcurrencyException)
                {
                    _logger.LogInformation(
                        "Discovery event already exists for {RequestId}; skipping publish.",
                        requestId.Value);
                    continue;
                }

                // Keep the projection in sync for subsequent slices.
                await _projectionUpdater.RebuildAndSaveAsync(requestId, cancellationToken).ConfigureAwait(false);

                await _publisher.PublishAsync(
                    eventType: EventTypes.RequestDiscovered,
                    subject: Subjects.ForRequest(requestId.Value),
                    eventId: eventId,
                    data: payload,
                    cancellationToken: cancellationToken).ConfigureAwait(false);

                published++;
            }
            finally
            {
                // Prevent correlation from leaking across work items in the same invocation.
                _correlation.Current = null;
            }
        }

        _logger.LogInformation(
            "Discovery completed. Claimed={Claimed} Published={Published}",
            claimed,
            published);
    }
}
