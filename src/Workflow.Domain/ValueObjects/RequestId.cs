namespace Workflow.Domain.ValueObjects;

/// <summary>
/// Identifies a workflow request across storage, events, and integration messages.
/// </summary>
/// <param name="Value">The canonical identifier (PartitionKey|RowKey).</param>
public readonly record struct RequestId(string Value)
{
    /// <summary>
    /// Creates a <see cref="RequestId"/> from Table Storage keys.
    /// </summary>
    public static RequestId FromTableKeys(string partitionKey, string rowKey)
        => new($"{partitionKey}|{rowKey}");

    /// <summary>
    /// Parses a canonical request id.
    /// </summary>
    /// <param name="value">Canonical value formatted as <c>PartitionKey|RowKey</c>.</param>
    /// <exception cref="ArgumentException">Thrown when the value is null/empty or malformed.</exception>
    public static RequestId Parse(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("RequestId is required.", nameof(value));
        }

        // Minimal validation: ensure there is exactly one separator.
        var idx = value.IndexOf('|');
        if (idx <= 0 || idx >= value.Length - 1 || value.LastIndexOf('|') != idx)
        {
            throw new ArgumentException("RequestId must be formatted as 'PartitionKey|RowKey'.", nameof(value));
        }

        return new RequestId(value);
    }
}
