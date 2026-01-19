using Workflow.Domain.ValueObjects;

namespace Workflow.Tests;

/// <summary>
/// Unit tests for value object formatting and invariants.
/// </summary>
public sealed class RequestIdTests
{
    [Fact]
    public void FromTableKeys_Formats_As_PartitionKey_Pipe_RowKey()
    {
        var id = RequestId.FromTableKeys("p", "r");

        Assert.Equal("p|r", id.Value);
    }
}
