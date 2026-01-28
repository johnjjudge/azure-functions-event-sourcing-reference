using System.Text.Json;
using Workflow.Application.Configuration;
using Workflow.Application.Contracts;
using Workflow.Application.EventSourcing;
using Workflow.Application.Projections;
using Workflow.Domain.Model;

namespace Workflow.Tests;

/// <summary>
/// Unit tests for <see cref="RequestProjectionReducer"/>.
/// </summary>
[TestClass]
public sealed class ProjectionReducerTests
{
    [TestMethod]
    public void RequestDiscovered_creates_InProgress_projection()
    {
        var reducer = new RequestProjectionReducer(new WorkflowOptions());

        var payload = new RequestDiscoveredPayload(
            RequestId: "p|r",
            PartitionKey: "p",
            RowKey: "r");

        var stored = ToStoredEvent(EventTypes.RequestDiscovered, payload, version: 1);

        var projection = reducer.Apply(null, stored);

        Assert.AreEqual("p|r", projection.Id);
        Assert.AreEqual("p|r", projection.RequestId.Value);
        Assert.AreEqual("p", projection.PartitionKey);
        Assert.AreEqual("r", projection.RowKey);
        Assert.AreEqual(WorkItemStatus.InProgress, projection.Status);
        Assert.AreEqual(0, projection.SubmitAttemptCount);
        Assert.AreEqual(1, projection.LastAppliedEventVersion);
    }

    [TestMethod]
    public void TerminalStatusReached_sets_Pass_or_Fail_and_clears_poll()
    {
        var reducer = new RequestProjectionReducer(new WorkflowOptions());

        var discovered = ToStoredEvent(EventTypes.RequestDiscovered,
            new RequestDiscoveredPayload("p|r", "p", "r"),
            version: 1);

        var submitted = ToStoredEvent(EventTypes.JobSubmitted,
            new JobSubmittedPayload("p|r", "p", "r", "job-123", Attempt: 1),
            version: 2);

        var terminal = ToStoredEvent(EventTypes.TerminalStatusReached,
            new TerminalStatusReachedPayload("p|r", "job-123", ExternalJobStatus.Pass, Attempt: 1),
            version: 3);

        var projection = reducer.ApplyAll(null, new[] { discovered, submitted, terminal });

        Assert.AreEqual(WorkItemStatus.Pass, projection.Status);
        Assert.IsNull(projection.NextPollAtUtc);
        Assert.AreEqual("job-123", projection.ExternalJobId?.Value);
        Assert.AreEqual(3, projection.LastAppliedEventVersion);
    }

    private static StoredEvent ToStoredEvent(string eventType, object payload, int version)
    {
        var json = JsonSerializer.SerializeToElement(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return new StoredEvent(
            EventId: $"e-{version}",
            EventType: eventType,
            OccurredUtc: DateTimeOffset.UtcNow,
            Data: json,
            CorrelationId: null,
            CausationId: null,
            Version: version);
    }
}
