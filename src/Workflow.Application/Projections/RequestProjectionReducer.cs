using System.Text.Json;
using Workflow.Application.Configuration;
using Workflow.Application.Contracts;
using Workflow.Application.EventSourcing;
using Workflow.Domain.Model;
using Workflow.Domain.ValueObjects;

namespace Workflow.Application.Projections;

/// <summary>
/// Reduces an ordered sequence of events into a <see cref="RequestProjection"/>.
/// </summary>
public sealed class RequestProjectionReducer
{
    private readonly JsonSerializerOptions _json;
    private readonly WorkflowOptions _options;

    /// <summary>
    /// Creates a new reducer.
    /// </summary>
    public RequestProjectionReducer(WorkflowOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
    }

    /// <summary>
    /// Applies one event to the current projection.
    /// </summary>
    /// <remarks>
    /// The reducer is monotonic: events with a version less than or equal to
    /// <see cref="RequestProjection.LastAppliedEventVersion"/> are ignored.
    /// </remarks>
    public RequestProjection Apply(RequestProjection? current, StoredEvent e)
    {
        if (e is null) throw new ArgumentNullException(nameof(e));

        if (current is not null && e.Version <= current.LastAppliedEventVersion)
        {
            return current;
        }

        return e.EventType switch
        {
            EventTypes.RequestDiscovered => Apply(current, Deserialize<RequestDiscoveredPayload>(e.Data), e),
            EventTypes.SubmissionPrepared => Apply(current, Deserialize<SubmissionPreparedPayload>(e.Data), e),
            EventTypes.JobSubmitted => Apply(current, Deserialize<JobSubmittedPayload>(e.Data), e),
            EventTypes.JobPollRequested => Apply(current, Deserialize<JobPollRequestedPayload>(e.Data), e),
            EventTypes.TerminalStatusReached => Apply(current, Deserialize<TerminalStatusReachedPayload>(e.Data), e),
            EventTypes.RequestCompleted => Apply(current, Deserialize<RequestCompletedPayload>(e.Data), e),
            _ => current ?? CreateUnknown(e),
        };
    }

    /// <summary>
    /// Applies an ordered sequence of events.
    /// </summary>
    public RequestProjection ApplyAll(RequestProjection? current, IEnumerable<StoredEvent> orderedEvents)
    {
        if (orderedEvents is null) throw new ArgumentNullException(nameof(orderedEvents));

        var state = current;
        foreach (var e in orderedEvents)
        {
            state = Apply(state, e);
        }

        return state ?? throw new InvalidOperationException("Projection reduction produced no state.");
    }

    private RequestProjection Apply(RequestProjection? current, RequestDiscoveredPayload payload, StoredEvent e)
    {
        var requestId = RequestId.Parse(payload.RequestId);

        // At discovery time, the Table row is already claimed and flipped to InProgress.
        return new RequestProjection
        {
            Id = payload.RequestId,
            RequestId = requestId,
            PartitionKey = payload.PartitionKey,
            RowKey = payload.RowKey,
            Status = WorkItemStatus.InProgress,
            SubmitAttemptCount = 0,
            NextPollAtUtc = null,
            ExternalJobId = null,
            LastAppliedEventVersion = e.Version,
            UpdatedUtc = e.OccurredUtc,
        };
    }

    private RequestProjection Apply(RequestProjection? current, SubmissionPreparedPayload payload, StoredEvent e)
    {
        var baseState = current ?? CreateFromPayload(payload, e);

        // If we are preparing a new attempt beyond what we previously observed,
        // clear the prior external job id and poll schedule. A new job will be
        // created by the SubmitJob handler and will establish a new poll cadence.
        var isNewAttempt = payload.Attempt > baseState.SubmitAttemptCount;

        return baseState with
        {
            Status = WorkItemStatus.InProgress,
            SubmitAttemptCount = Math.Max(baseState.SubmitAttemptCount, payload.Attempt),
            ExternalJobId = isNewAttempt ? null : baseState.ExternalJobId,
            NextPollAtUtc = isNewAttempt ? null : baseState.NextPollAtUtc,
            LastAppliedEventVersion = e.Version,
            UpdatedUtc = e.OccurredUtc,
        };
    }

    private RequestProjection Apply(RequestProjection? current, JobSubmittedPayload payload, StoredEvent e)
    {
        var baseState = current ?? CreateFromPayload(payload, e);

        return baseState with
        {
            Status = WorkItemStatus.InProgress,
            SubmitAttemptCount = Math.Max(baseState.SubmitAttemptCount, payload.Attempt),
            ExternalJobId = new ExternalJobId(payload.ExternalJobId),
            NextPollAtUtc = e.OccurredUtc + _options.PollInterval,
            LastAppliedEventVersion = e.Version,
            UpdatedUtc = e.OccurredUtc,
        };
    }

    private RequestProjection Apply(RequestProjection? current, JobPollRequestedPayload payload, StoredEvent e)
    {
        var baseState = current ?? CreateFromPayload(payload, e);

        // A poll request implies the job should be checked now, and then re-scheduled.
        // Scheduler uses NextPollAtUtc for query; this marks the next due time.
        return baseState with
        {
            NextPollAtUtc = e.OccurredUtc + _options.PollInterval,
            LastAppliedEventVersion = e.Version,
            UpdatedUtc = e.OccurredUtc,
        };
    }

    private RequestProjection Apply(RequestProjection? current, TerminalStatusReachedPayload payload, StoredEvent e)
    {
        var baseState = current ?? CreateFromPayload(payload, e);

        var final = payload.TerminalStatus switch
        {
            ExternalJobStatus.Pass => WorkItemStatus.Pass,
            ExternalJobStatus.Fail => WorkItemStatus.Fail,
            _ => WorkItemStatus.Fail,
        };

        return baseState with
        {
            Status = final,
            NextPollAtUtc = null,
            SubmitAttemptCount = Math.Max(baseState.SubmitAttemptCount, payload.Attempt),
            ExternalJobId = new ExternalJobId(payload.ExternalJobId),
            LastAppliedEventVersion = e.Version,
            UpdatedUtc = e.OccurredUtc,
        };
    }

    private RequestProjection Apply(RequestProjection? current, RequestCompletedPayload payload, StoredEvent e)
    {
        var baseState = current ?? new RequestProjection
        {
            Id = payload.RequestId,
            RequestId = RequestId.Parse(payload.RequestId),
            PartitionKey = "",
            RowKey = "",
            Status = payload.FinalStatus,
            SubmitAttemptCount = 0,
            NextPollAtUtc = null,
            ExternalJobId = null,
            LastAppliedEventVersion = e.Version,
            UpdatedUtc = e.OccurredUtc,
        };

        return baseState with
        {
            Status = payload.FinalStatus,
            NextPollAtUtc = null,
            LastAppliedEventVersion = e.Version,
            UpdatedUtc = e.OccurredUtc,
        };
    }

    private RequestProjection CreateFromPayload(SubmissionPreparedPayload payload, StoredEvent e)
        => new()
        {
            Id = payload.RequestId,
            RequestId = RequestId.Parse(payload.RequestId),
            PartitionKey = payload.PartitionKey,
            RowKey = payload.RowKey,
            Status = WorkItemStatus.InProgress,
            SubmitAttemptCount = payload.Attempt,
            NextPollAtUtc = null,
            ExternalJobId = null,
            LastAppliedEventVersion = e.Version,
            UpdatedUtc = e.OccurredUtc,
        };

    private RequestProjection CreateFromPayload(JobSubmittedPayload payload, StoredEvent e)
        => new()
        {
            Id = payload.RequestId,
            RequestId = RequestId.Parse(payload.RequestId),
            PartitionKey = payload.PartitionKey,
            RowKey = payload.RowKey,
            Status = WorkItemStatus.InProgress,
            SubmitAttemptCount = payload.Attempt,
            NextPollAtUtc = e.OccurredUtc + _options.PollInterval,
            ExternalJobId = new ExternalJobId(payload.ExternalJobId),
            LastAppliedEventVersion = e.Version,
            UpdatedUtc = e.OccurredUtc,
        };

    private RequestProjection CreateFromPayload(JobPollRequestedPayload payload, StoredEvent e)
        => new()
        {
            Id = payload.RequestId,
            RequestId = RequestId.Parse(payload.RequestId),
            PartitionKey = "",
            RowKey = "",
            Status = WorkItemStatus.InProgress,
            SubmitAttemptCount = payload.Attempt,
            NextPollAtUtc = e.OccurredUtc + _options.PollInterval,
            ExternalJobId = new ExternalJobId(payload.ExternalJobId),
            LastAppliedEventVersion = e.Version,
            UpdatedUtc = e.OccurredUtc,
        };

    private RequestProjection CreateFromPayload(TerminalStatusReachedPayload payload, StoredEvent e)
        => new()
        {
            Id = payload.RequestId,
            RequestId = RequestId.Parse(payload.RequestId),
            PartitionKey = "",
            RowKey = "",
            Status = payload.TerminalStatus == ExternalJobStatus.Pass ? WorkItemStatus.Pass : WorkItemStatus.Fail,
            SubmitAttemptCount = payload.Attempt,
            NextPollAtUtc = null,
            ExternalJobId = new ExternalJobId(payload.ExternalJobId),
            LastAppliedEventVersion = e.Version,
            UpdatedUtc = e.OccurredUtc,
        };

    private RequestProjection CreateUnknown(StoredEvent e)
        => new()
        {
            Id = "unknown",
            RequestId = RequestId.Parse("unknown|unknown"),
            PartitionKey = "",
            RowKey = "",
            Status = WorkItemStatus.InProgress,
            SubmitAttemptCount = 0,
            NextPollAtUtc = null,
            ExternalJobId = null,
            LastAppliedEventVersion = e.Version,
            UpdatedUtc = e.OccurredUtc,
        };

    private T Deserialize<T>(JsonElement data)
        => JsonSerializer.Deserialize<T>(data.GetRawText(), _json)
           ?? throw new JsonException($"Unable to deserialize payload for {typeof(T).Name}.");
}
