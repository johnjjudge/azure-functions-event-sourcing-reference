using System.Text.Json;
using System.Linq;
using Workflow.Application.Contracts;
using Workflow.Application.EventSourcing;
using Workflow.Domain.Model;
using Workflow.Domain.ValueObjects;

namespace Workflow.Application.Domain;

/// <summary>
/// Event-sourced domain model for a single workflow request.
/// </summary>
/// <remarks>
/// <para>
/// This aggregate is reconstructed by replaying the event stream stored in Cosmos DB.
/// It is intentionally minimal: it exists to demonstrate event sourcing and enforce
/// invariants for later workflow steps.
/// </para>
/// <para>
/// The system assumes at-least-once event delivery, so this model is used in combination
/// with idempotency to ensure handlers are safe.
/// </para>
/// </remarks>
public sealed class RequestAggregate
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly HashSet<int> _preparedAttempts = new();
    private readonly HashSet<int> _submittedAttempts = new();

    /// <summary>
    /// Gets the request identifier.
    /// </summary>
    public RequestId RequestId { get; }

    /// <summary>
    /// Gets the Table Storage keys associated with the request.
    /// </summary>
    public TableKeys Keys { get; private set; }

    /// <summary>
    /// Gets the current workflow status.
    /// </summary>
    public WorkItemStatus Status { get; private set; } = WorkItemStatus.Unprocessed;

    /// <summary>
    /// Gets the number of submit attempts that have been executed (i.e., <c>JobSubmitted</c> events).
    /// </summary>
    public int SubmitAttemptCount { get; private set; }

    /// <summary>
    /// Gets the latest external job id, if one has been created.
    /// </summary>
    public ExternalJobId? ExternalJobId { get; private set; }

    /// <summary>
    /// Gets the current stream version of the aggregate.
    /// </summary>
    public int Version { get; private set; }

    private RequestAggregate(RequestId requestId)
    {
        RequestId = requestId;
        Keys = new TableKeys(string.Empty, string.Empty);
    }

    /// <summary>
    /// Reconstructs an aggregate from its event stream.
    /// </summary>
    public static RequestAggregate Rehydrate(RequestId requestId, IReadOnlyList<StoredEvent> history)
    {
        var aggregate = new RequestAggregate(requestId);
        foreach (var e in history.OrderBy(x => x.Version))
        {
            aggregate.Apply(e);
        }

        aggregate.Version = history.LastOrDefault()?.Version ?? 0;
        return aggregate;
    }

    /// <summary>
    /// Returns <c>true</c> if a submission has already been prepared for the given attempt number.
    /// </summary>
    public bool HasPreparedSubmission(int attempt) => _preparedAttempts.Contains(attempt);

    /// <summary>
    /// Returns <c>true</c> if a job has already been submitted for the given attempt number.
    /// </summary>
    public bool HasSubmittedJob(int attempt) => _submittedAttempts.Contains(attempt);

    private void Apply(StoredEvent e)
    {
        Version = e.Version;

        switch (e.EventType)
        {
            case EventTypes.RequestDiscovered:
            {
                var payload = Deserialize<RequestDiscoveredPayload>(e.Data);
                Keys = new TableKeys(payload.PartitionKey, payload.RowKey);
                Status = WorkItemStatus.InProgress;
                break;
            }

            case EventTypes.SubmissionPrepared:
            {
                var payload = Deserialize<SubmissionPreparedPayload>(e.Data);
                _preparedAttempts.Add(payload.Attempt);
                // Preparing does not change the Table Storage-facing status.
                break;
            }

            case EventTypes.JobSubmitted:
            {
                var payload = Deserialize<JobSubmittedPayload>(e.Data);
                _submittedAttempts.Add(payload.Attempt);
                SubmitAttemptCount = Math.Max(SubmitAttemptCount, payload.Attempt);
                ExternalJobId = new ExternalJobId(payload.ExternalJobId);
                Status = WorkItemStatus.InProgress;
                break;
            }

            case EventTypes.TerminalStatusReached:
            {
                var payload = Deserialize<TerminalStatusReachedPayload>(e.Data);
                Status = payload.TerminalStatus switch
                {
                    ExternalJobStatus.Pass => WorkItemStatus.Pass,
                    ExternalJobStatus.Fail => WorkItemStatus.Fail,
                    _ => Status,
                };
                break;
            }

            case EventTypes.RequestCompleted:
            {
                var payload = Deserialize<RequestCompletedPayload>(e.Data);
                Status = payload.FinalStatus;
                break;
            }
        }
    }

    private static T Deserialize<T>(JsonElement json)
        => json.Deserialize<T>(SerializerOptions)
           ?? throw new InvalidOperationException($"Failed to deserialize {typeof(T).Name} from event payload.");
}
