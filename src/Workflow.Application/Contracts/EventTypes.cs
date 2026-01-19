namespace Workflow.Application.Contracts;

/// <summary>
/// Integration event type names published to Event Grid.
/// </summary>
/// <remarks>
/// Event types are versioned to allow non-breaking evolution.
/// </remarks>
public static class EventTypes
{
    /// <summary>
    /// Emitted when an unprocessed Table Storage row has been claimed and introduced into the workflow.
    /// </summary>
    public const string RequestDiscovered = "workflow.request.discovered.v1";

    /// <summary>
    /// Emitted when a request has been prepared for submission to the external service.
    /// </summary>
    public const string SubmissionPrepared = "workflow.submission.prepared.v1";

    /// <summary>
    /// Emitted when a job has been submitted to the external service.
    /// </summary>
    public const string JobSubmitted = "workflow.job.submitted.v1";

    /// <summary>
    /// Emitted to request a status poll of an external job.
    /// </summary>
    public const string JobPollRequested = "workflow.job.pollrequested.v1";

    /// <summary>
    /// Emitted when the external service reaches a terminal status for the job.
    /// </summary>
    public const string TerminalStatusReached = "workflow.job.terminal.v1";

    /// <summary>
    /// Emitted when the original Table Storage row has been updated to its completed state.
    /// </summary>
    public const string RequestCompleted = "workflow.request.completed.v1";
}
