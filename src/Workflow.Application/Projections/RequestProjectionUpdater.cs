using Workflow.Application.Abstractions;
using Workflow.Domain.ValueObjects;

namespace Workflow.Application.Projections;

/// <summary>
/// Service that builds and persists projections from the underlying event store.
/// </summary>
/// <remarks>
/// Projections are rebuildable; this service can be used by handlers to ensure
/// the query model reflects the latest stream state.
/// </remarks>
public sealed class RequestProjectionUpdater
{
    private readonly IEventStore _eventStore;
    private readonly IRequestProjectionRepository _repository;
    private readonly RequestProjectionReducer _reducer;

    /// <summary>
    /// Creates a new instance.
    /// </summary>
    public RequestProjectionUpdater(IEventStore eventStore, IRequestProjectionRepository repository, RequestProjectionReducer reducer)
    {
        _eventStore = eventStore ?? throw new ArgumentNullException(nameof(eventStore));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _reducer = reducer ?? throw new ArgumentNullException(nameof(reducer));
    }

    /// <summary>
    /// Rebuilds the projection for the given request id using the event stream and upserts it.
    /// </summary>
    public async Task<RequestProjection> RebuildAndSaveAsync(RequestId requestId, CancellationToken cancellationToken)
    {
        var current = await _repository.GetAsync(requestId, cancellationToken);
        var events = await _eventStore.ReadStreamAsync(requestId, cancellationToken);
        var reduced = _reducer.ApplyAll(current, events);
        await _repository.UpsertAsync(reduced, cancellationToken);
        return reduced;
    }
}
