using Workflow.Domain.ValueObjects;

namespace Workflow.Tests;

/// <summary>
/// Unit tests for value object formatting and invariants.
/// </summary>
[TestClass]
public sealed class RequestIdTests
{
    [TestMethod]
    public void FromTableKeys_Formats_As_PartitionKey_Pipe_RowKey()
    {
        var id = RequestId.FromTableKeys("p", "r");

        Assert.AreEqual("p|r", id.Value);
    }
}
