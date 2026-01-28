using Azure;
using Azure.Data.Tables;
using Workflow.Application.Abstractions;
using Workflow.Domain.Model;
using Workflow.Domain.ValueObjects;

namespace Workflow.Infrastructure.Tables;

/// <summary>
/// Azure Table Storage implementation of <see cref="ITableIntakeRepository"/>.
/// </summary>
public sealed class TableIntakeRepository : ITableIntakeRepository
{
    private readonly TableClient _table;

    /// <summary>
    /// Initializes a new instance of the <see cref="TableIntakeRepository"/> class.
    /// </summary>
    /// <param name="tableClient">Table client.</param>
    public TableIntakeRepository(TableClient tableClient)
    {
        _table = tableClient;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<IntakeRow>> GetAvailableUnprocessedAsync(int take, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        // OData filter: (Status == 'Unprocessed' OR Status == 'InProgress') AND LeaseUntilUtc <= now
        // This allows expired leases to be reclaimed while excluding terminal rows.
        // Azure Tables expects UTC timestamps. Use a Z-suffixed format (no offset component).
        var nowZ = nowUtc.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ");
        var filter = $"(Status eq '{WorkItemStatus.Unprocessed}' or Status eq '{WorkItemStatus.InProgress}') and LeaseUntilUtc le datetime'{nowZ}'";

        var results = new List<IntakeRow>(capacity: Math.Max(0, take));

        await foreach (var entity in _table.QueryAsync<IntakeWorkItemEntity>(filter: filter, maxPerPage: take, cancellationToken: cancellationToken))
        {
            results.Add(ToRow(entity));
            if (results.Count >= take)
            {
                break;
            }
        }

        return results;
    }

    /// <inheritdoc />
    public async Task<bool> TryClaimAsync(IntakeRow row, DateTimeOffset leaseUntilUtc, CancellationToken cancellationToken)
    {
        var nowUtc = DateTimeOffset.UtcNow;
        var isEligible = row.Status == WorkItemStatus.Unprocessed
            || (row.Status == WorkItemStatus.InProgress && row.LeaseUntilUtc <= nowUtc);

        // Defensive check: only claim eligible rows. This prevents accidental transitions
        // if upstream logic changes or the lease has not yet expired.
        if (!isEligible)
        {
            return false;
        }

        var entity = new IntakeWorkItemEntity
        {
            PartitionKey = row.Keys.PartitionKey,
            RowKey = row.Keys.RowKey,
            Status = WorkItemStatus.InProgress.ToString(),
            LeaseUntilUtc = leaseUntilUtc,
            UpdatedUtc = DateTimeOffset.UtcNow,
            ETag = new ETag(row.ETag)
        };

        try
        {
            await _table.UpdateEntityAsync(entity, entity.ETag, TableUpdateMode.Merge, cancellationToken);
            return true;
        }
        catch (RequestFailedException ex) when (ex.Status == 412)
        {
            // Precondition failed: the row was modified by another worker.
            return false;
        }
    }

    /// <inheritdoc />
    public async Task MarkTerminalAsync(TableKeys keys, WorkItemStatus terminalStatus, CancellationToken cancellationToken)
    {
        if (terminalStatus is not (WorkItemStatus.Pass or WorkItemStatus.Fail))
        {
            throw new ArgumentException("Terminal status must be Pass or Fail.", nameof(terminalStatus));
        }

        // We intentionally do not supply an ETag here. This is a "force" update: terminal status is authoritative.
        // In later slices the workflow will ensure idempotency and monotonic transitions using the event store.
        var entity = new IntakeWorkItemEntity
        {
            PartitionKey = keys.PartitionKey,
            RowKey = keys.RowKey,
            Status = terminalStatus.ToString(),
            LeaseUntilUtc = DateTimeOffset.MinValue,
            UpdatedUtc = DateTimeOffset.UtcNow,
            ETag = ETag.All
        };

        await _table.UpdateEntityAsync(entity, entity.ETag, TableUpdateMode.Merge, cancellationToken);
    }

    private static IntakeRow ToRow(IntakeWorkItemEntity entity)
    {
        var status = Enum.TryParse<WorkItemStatus>(entity.Status, ignoreCase: true, out var parsed)
            ? parsed
            : WorkItemStatus.Unprocessed;

        return new IntakeRow(
            Keys: new TableKeys(entity.PartitionKey, entity.RowKey),
            Status: status,
            LeaseUntilUtc: entity.LeaseUntilUtc,
            ETag: entity.ETag.ToString());
    }
}
