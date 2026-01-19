using Workflow.Domain.Model;
using Workflow.Domain.ValueObjects;

namespace Workflow.Application.Projections;

/// <summary>
/// Lightweight, rebuildable read model derived from the request event stream.
/// </summary>
/// <remarks>
/// This projection exists to enable operational queries (e.g., "which jobs should be polled now?")
/// without scanning or replaying all event streams.
/// </remarks>
public sealed record RequestProjection
{
    /// <summary>
    /// Cosmos document id.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Canonical request identifier formatted as <c>PartitionKey|RowKey</c>.
    /// </summary>
    public required RequestId RequestId { get; init; }

    /// <summary>
    /// Table Storage partition key.
    /// </summary>
    public required string PartitionKey { get; init; }

    /// <summary>
    /// Table Storage row key.
    /// </summary>
    public required string RowKey { get; init; }

    /// <summary>
    /// Current workflow status.
    /// </summary>
    public required WorkItemStatus Status { get; init; }

    /// <summary>
    /// 1-based submission attempt count.
    /// </summary>
    public int SubmitAttemptCount { get; init; }

    /// <summary>
    /// When the next external status poll is due.
    /// </summary>
    public DateTimeOffset? NextPollAtUtc { get; init; }

    /// <summary>
    /// External job id returned by the external service.
    /// </summary>
    public ExternalJobId? ExternalJobId { get; init; }

    /// <summary>
    /// Latest stream version that has been applied to this projection.
    /// </summary>
    public int LastAppliedEventVersion { get; init; }

    /// <summary>
    /// When the projection was last updated (UTC).
    /// </summary>
    public DateTimeOffset UpdatedUtc { get; init; }

    /// <summary>
    /// Convenience wrapper for the source table keys.
    /// </summary>
    public TableKeys Keys => new(PartitionKey, RowKey);
}
