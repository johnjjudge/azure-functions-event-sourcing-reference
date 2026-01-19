using Workflow.Domain.Model;

namespace Workflow.Application.Abstractions;

/// <summary>
/// A client for interacting with the external service used by this reference implementation.
/// </summary>
/// <remarks>
/// The workflow is designed around at-least-once event delivery, so callers should assume
/// this client may be invoked multiple times for the same logical operation.
/// </remarks>
public interface IExternalServiceClient
{
    /// <summary>
    /// Creates a new external job for the given request.
    /// </summary>
    /// <param name="requestId">The workflow request identifier.</param>
    /// <param name="attempt">The 1-based attempt count for submissions.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created job identifier and its initial status.</returns>
    Task<(ExternalJobId JobId, ExternalJobStatus Status)> CreateJobAsync(
        RequestId requestId,
        int attempt,
        CancellationToken cancellationToken);

    /// <summary>
    /// Retrieves the current status of an external job.
    /// </summary>
    /// <param name="jobId">External job identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The external job status.</returns>
    Task<ExternalJobStatus> GetStatusAsync(ExternalJobId jobId, CancellationToken cancellationToken);
}
