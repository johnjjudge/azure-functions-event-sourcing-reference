using Workflow.Domain.ValueObjects;

namespace Workflow.Domain.Events;

/// <summary>
/// Raised when an unprocessed intake row is discovered and claimed for processing.
/// </summary>
public sealed record RequestDiscovered(RequestId RequestId, string PartitionKey, string RowKey) : DomainEvent;
