using Workflow.Application.Projections;
using Workflow.Domain.ValueObjects;

namespace Workflow.Application.Abstractions;

/// <summary>
/// Stores and queries a lightweight, rebuildable projection derived from the event stream.
/// </summary>
public interface IRequestProjectionRepository
{
    /// <summary>
    /// Upserts projection state for a request.
    /// </summary>
    Task UpsertAsync(RequestProjection projection, CancellationToken cancellationToken);

    /// <summary>
    /// Gets a projection by request id.
    /// </summary>
    Task<RequestProjection?> GetAsync(RequestId requestId, CancellationToken cancellationToken);

    /// <summary>
    /// Queries projections that require polling.
    /// </summary>
    Task<IReadOnlyList<RequestProjection>> GetDueForPollAsync(DateTimeOffset nowUtc, int take, CancellationToken cancellationToken);
}
