using Azure;
using Azure.Data.Tables;
using Workflow.Domain.Model;

namespace Workflow.Infrastructure.Tables;

/// <summary>
/// Azure Table Storage entity representing a single workflow intake work item.
/// </summary>
/// <remarks>
/// To keep Table queries efficient and avoid null-handling edge cases in OData filters,
/// <see cref="LeaseUntilUtc"/> is always present and uses <see cref="DateTimeOffset.MinValue"/>
/// to represent "no lease".
/// </remarks>
public sealed class IntakeWorkItemEntity : ITableEntity
{
    /// <inheritdoc />
    public string PartitionKey { get; set; } = string.Empty;

    /// <inheritdoc />
    public string RowKey { get; set; } = string.Empty;

    /// <summary>
    /// Work status. Stored as a string for readability.
    /// </summary>
    public string Status { get; set; } = WorkItemStatus.Unprocessed.ToString();

    /// <summary>
    /// Lease expiration time in UTC. <see cref="DateTimeOffset.MinValue"/> indicates no active lease.
    /// </summary>
    public DateTimeOffset LeaseUntilUtc { get; set; } = DateTimeOffset.MinValue;

    /// <summary>
    /// Last time the entity was updated (UTC).
    /// </summary>
    public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <inheritdoc />
    public DateTimeOffset? Timestamp { get; set; }

    /// <inheritdoc />
    public ETag ETag { get; set; }
}
