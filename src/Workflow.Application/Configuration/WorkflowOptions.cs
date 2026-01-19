namespace Workflow.Application.Configuration;

/// <summary>
/// Workflow tuning parameters.
/// </summary>
public sealed class WorkflowOptions
{
    /// <summary>
    /// Maximum number of intake rows the discovery function will attempt to claim per invocation.
    /// </summary>
    public int IntakeBatchSize { get; init; } = 50;

    /// <summary>
    /// Maximum number of requests the poll scheduler will emit poll events for per invocation.
    /// </summary>
    public int PollBatchSize { get; init; } = 200;

    /// <summary>
    /// Lease duration applied when a row is claimed from Table Storage.
    /// </summary>
    /// <remarks>
    /// The lease prevents other workers from claiming the same work item for a bounded time window.
    /// If a worker crashes or becomes unhealthy, the item becomes eligible again once the lease expires.
    /// </remarks>
    public TimeSpan LeaseDuration { get; init; } = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Poll interval used by the poll scheduler.
    /// </summary>
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Maximum number of submission attempts before the workflow stops retrying.
    /// </summary>
    public int MaxSubmitAttempts { get; init; } = 3;

    /// <summary>
    /// Lease duration used by idempotent event handlers.
    /// </summary>
    /// <remarks>
    /// Event Grid delivery is at-least-once. Handlers use <see cref="Abstractions.IIdempotencyStore"/>
    /// to ensure a given incoming event is processed exactly once with respect to side effects.
    /// If a handler crashes while holding a lease, another invocation may take over after expiry.
    /// </remarks>
    public TimeSpan IdempotencyLeaseDuration { get; init; } = TimeSpan.FromMinutes(2);
}
