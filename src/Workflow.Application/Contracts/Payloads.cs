using Workflow.Domain.Model;

namespace Workflow.Application.Contracts;

/// <summary>
/// Payload emitted when an intake row is claimed and introduced into the workflow.
/// </summary>
/// <param name="RequestId">The canonical request id, formatted as PartitionKey|RowKey.</param>
/// <param name="PartitionKey">The Table Storage partition key.</param>
/// <param name="RowKey">The Table Storage row key.</param>
public sealed record RequestDiscoveredPayload(
    string RequestId,
    string PartitionKey,
    string RowKey);

/// <summary>
/// Payload emitted when the workflow has prepared an external submission.
/// </summary>
/// <param name="RequestId">The canonical request id, formatted as PartitionKey|RowKey.</param>
/// <param name="PartitionKey">The Table Storage partition key.</param>
/// <param name="RowKey">The Table Storage row key.</param>
/// <param name="Attempt">The submission attempt number (1-based).</param>
public sealed record SubmissionPreparedPayload(
    string RequestId,
    string PartitionKey,
    string RowKey,
    int Attempt);

/// <summary>
/// Payload emitted after the workflow submits a job to the external service.
/// </summary>
/// <param name="RequestId">The canonical request id, formatted as PartitionKey|RowKey.</param>
/// <param name="PartitionKey">The Table Storage partition key.</param>
/// <param name="RowKey">The Table Storage row key.</param>
/// <param name="ExternalJobId">The job identifier returned by the external service.</param>
/// <param name="Attempt">The submission attempt number (1-based).</param>
public sealed record JobSubmittedPayload(
    string RequestId,
    string PartitionKey,
    string RowKey,
    string ExternalJobId,
    int Attempt);

/// <summary>
/// Payload emitted to request a status poll of the external job.
/// </summary>
/// <param name="RequestId">The canonical request id, formatted as PartitionKey|RowKey.</param>
/// <param name="ExternalJobId">The job identifier returned by the external service.</param>
/// <param name="Attempt">The submission attempt number (1-based).</param>
public sealed record JobPollRequestedPayload(
    string RequestId,
    string ExternalJobId,
    int Attempt);

/// <summary>
/// Payload emitted when the external service reaches a terminal status for the job.
/// </summary>
/// <param name="RequestId">The canonical request id, formatted as PartitionKey|RowKey.</param>
/// <param name="ExternalJobId">The external job identifier.</param>
/// <param name="TerminalStatus">The terminal status reported by the external service.</param>
/// <param name="Attempt">The submission attempt number that led to this terminal status (1-based).</param>
public sealed record TerminalStatusReachedPayload(
    string RequestId,
    string ExternalJobId,
    ExternalJobStatus TerminalStatus,
    int Attempt);

/// <summary>
/// Payload emitted after the workflow updates the intake work item to its completed state.
/// </summary>
/// <param name="RequestId">The canonical request id, formatted as PartitionKey|RowKey.</param>
/// <param name="FinalStatus">The final status written back to Table Storage.</param>
public sealed record RequestCompletedPayload(
    string RequestId,
    WorkItemStatus FinalStatus);
