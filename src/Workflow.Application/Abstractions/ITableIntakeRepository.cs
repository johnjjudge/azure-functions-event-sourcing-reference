using Workflow.Domain.Model;
using Workflow.Domain.ValueObjects;

namespace Workflow.Application.Abstractions;

/// <summary>
/// Repository for reading and updating intake rows stored in Azure Table Storage.
/// </summary>
/// <remarks>
/// <para>
/// The workflow uses Table Storage as an "intake" mechanism where each row represents a work item to process.
/// A timer-triggered function periodically searches for <see cref="WorkItemStatus.Unprocessed"/> rows,
/// then claims them using optimistic concurrency before emitting domain/integration events.
/// </para>
/// <para>
/// Azure Functions and Event Grid provide at-least-once delivery semantics; handlers must therefore be idempotent.
/// Claiming rows in Table Storage is an additional guard to reduce duplicate processing and enables recovery via
/// a time-bounded lease (<see cref="IntakeRow.LeaseUntilUtc"/>).
/// </para>
/// </remarks>
public interface ITableIntakeRepository
{
    /// <summary>
    /// Returns up to <paramref name="take"/> rows that are eligible to be claimed.
    /// </summary>
    /// <param name="take">Maximum number of rows to return.</param>
    /// <param name="nowUtc">Current time in UTC, used to evaluate lease expiration.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<IntakeRow>> GetAvailableUnprocessedAsync(
        int take,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken);

    /// <summary>
    /// Attempts to claim a row by transitioning it from <see cref="WorkItemStatus.Unprocessed"/> to
    /// <see cref="WorkItemStatus.InProgress"/> and setting a lease expiration time.
    /// </summary>
    /// <param name="row">The row to claim.</param>
    /// <param name="leaseUntilUtc">Lease expiration in UTC.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// <c>true</c> if the claim succeeded; <c>false</c> if the row was modified by another worker.
    /// </returns>
    Task<bool> TryClaimAsync(IntakeRow row, DateTimeOffset leaseUntilUtc, CancellationToken cancellationToken);

    /// <summary>
    /// Marks a row terminal as <see cref="WorkItemStatus.Pass"/> or <see cref="WorkItemStatus.Fail"/>
    /// and clears any active lease.
    /// </summary>
    /// <param name="keys">Partition and row keys for the table row.</param>
    /// <param name="terminalStatus">Terminal status value.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task MarkTerminalAsync(TableKeys keys, WorkItemStatus terminalStatus, CancellationToken cancellationToken);
}

/// <summary>
/// Minimal representation of an intake row, including concurrency and lease information.
/// </summary>
/// <param name="Keys">PartitionKey and RowKey.</param>
/// <param name="Status">Current work status.</param>
/// <param name="LeaseUntilUtc">Lease expiration in UTC. If <= now, the row is eligible for claiming.</param>
/// <param name="ETag">Row ETag used for optimistic concurrency.</param>
public sealed record IntakeRow(
    TableKeys Keys,
    WorkItemStatus Status,
    DateTimeOffset LeaseUntilUtc,
    string ETag);
