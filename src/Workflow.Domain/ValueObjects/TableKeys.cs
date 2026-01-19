namespace Workflow.Domain.ValueObjects;

/// <summary>
/// Represents the primary key components of an Azure Table Storage entity.
/// </summary>
/// <param name="PartitionKey">Partition key.</param>
/// <param name="RowKey">Row key.</param>
public readonly record struct TableKeys(string PartitionKey, string RowKey)
{
    /// <summary>
    /// Creates canonical <see cref="RequestId"/> from these keys.
    /// </summary>
    public RequestId ToRequestId() => RequestId.FromTableKeys(PartitionKey, RowKey);
}
