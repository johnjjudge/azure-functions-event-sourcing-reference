namespace Workflow.Domain.Model;

/// <summary>
/// Status values returned by the external service for a submitted job.
/// </summary>
/// <remarks>
/// These names intentionally match the wire values used by the sample external service.
/// </remarks>
public enum ExternalJobStatus
{
    /// <summary>
    /// The job has been created by the external service but has not yet begun processing.
    /// </summary>
    Created = 0,

    /// <summary>
    /// The job is currently being processed by the external service.
    /// </summary>
    Inprogress = 1,

    /// <summary>
    /// The job completed successfully.
    /// </summary>
    Pass = 2,

    /// <summary>
    /// The job completed with a terminal failure.
    /// </summary>
    Fail = 3,

    /// <summary>
    /// The job failed in a way that can be retried by re-submitting the job.
    /// </summary>
    FailCanRetry = 4,
}
