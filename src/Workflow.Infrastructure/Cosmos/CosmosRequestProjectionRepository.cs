using System.Net;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;
using Workflow.Application.Abstractions;
using Workflow.Application.Projections;
using Workflow.Domain.Model;
using Workflow.Domain.ValueObjects;
using Workflow.Infrastructure.Configuration;

namespace Workflow.Infrastructure.Cosmos;

/// <summary>
/// Cosmos DB implementation of <see cref="IRequestProjectionRepository"/>.
/// </summary>
/// <remarks>
/// The projection container is partitioned by request id (<c>/requestId</c>)
/// and is treated as a rebuildable read model derived from the event stream.
/// </remarks>
internal sealed class CosmosRequestProjectionRepository : IRequestProjectionRepository
{
    private readonly Container _container;

    /// <summary>
    /// Creates a new instance.
    /// </summary>
    public CosmosRequestProjectionRepository(CosmosClient client, IOptions<CosmosDbOptions> options)
    {
        var o = options.Value;
        _container = client.GetContainer(o.DatabaseName, o.ProjectionsContainerName);
    }

    /// <inheritdoc />
    public async Task UpsertAsync(RequestProjection projection, CancellationToken cancellationToken)
    {
        if (projection is null) throw new ArgumentNullException(nameof(projection));

        var doc = CosmosProjectionDocument.FromProjection(projection);

        await _container.UpsertItemAsync(
            item: doc,
            partitionKey: new PartitionKey(doc.RequestId),
            cancellationToken: cancellationToken);
    }

    /// <inheritdoc />
    public async Task<RequestProjection?> GetAsync(RequestId requestId, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _container.ReadItemAsync<CosmosProjectionDocument>(
                id: requestId.Value,
                partitionKey: new PartitionKey(requestId.Value),
                cancellationToken: cancellationToken);

            return response.Resource.ToProjection();
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RequestProjection>> GetDueForPollAsync(DateTimeOffset nowUtc, int take, CancellationToken cancellationToken)
    {
        if (take <= 0) return Array.Empty<RequestProjection>();

        // NOTE: we query across partitions. Keep the predicate selective.
        var query = new QueryDefinition(
                "SELECT TOP @take * FROM c WHERE c.status = @status AND IS_DEFINED(c.nextPollAtUtc) AND NOT IS_NULL(c.nextPollAtUtc) AND c.nextPollAtUtc <= @now")
            .WithParameter("@take", take)
            .WithParameter("@status", WorkItemStatus.InProgress.ToString())
            .WithParameter("@now", nowUtc);

        var iterator = _container.GetItemQueryIterator<CosmosProjectionDocument>(query);

        var list = new List<RequestProjection>(take);
        while (iterator.HasMoreResults && list.Count < take)
        {
            var page = await iterator.ReadNextAsync(cancellationToken);
            foreach (var item in page)
            {
                list.Add(item.ToProjection());
                if (list.Count >= take) break;
            }
        }

        return list;
    }

    private sealed class CosmosProjectionDocument
    {
        public required string Id { get; init; }

        // partition key
        public required string RequestId { get; init; }

        public required string PartitionKey { get; init; }
        public required string RowKey { get; init; }

        public required string Status { get; init; }

        public int SubmitAttemptCount { get; init; }

        public DateTimeOffset? NextPollAtUtc { get; init; }

        public string? ExternalJobId { get; init; }

        public int LastAppliedEventVersion { get; init; }

        public DateTimeOffset UpdatedUtc { get; init; }

        public static CosmosProjectionDocument FromProjection(RequestProjection projection)
            => new()
            {
                Id = projection.Id,
                RequestId = projection.RequestId.Value,
                PartitionKey = projection.PartitionKey,
                RowKey = projection.RowKey,
                Status = projection.Status.ToString(),
                SubmitAttemptCount = projection.SubmitAttemptCount,
                NextPollAtUtc = projection.NextPollAtUtc,
                ExternalJobId = projection.ExternalJobId?.Value,
                LastAppliedEventVersion = projection.LastAppliedEventVersion,
                UpdatedUtc = projection.UpdatedUtc,
            };

        public RequestProjection ToProjection()
            => new()
            {
                Id = Id,
                RequestId = Domain.ValueObjects.RequestId.Parse(RequestId),
                PartitionKey = PartitionKey,
                RowKey = RowKey,
                Status = Enum.TryParse<WorkItemStatus>(Status, ignoreCase: true, out var s) ? s : WorkItemStatus.InProgress,
                SubmitAttemptCount = SubmitAttemptCount,
                NextPollAtUtc = NextPollAtUtc,
                ExternalJobId = ExternalJobId is null ? null : new ExternalJobId(ExternalJobId),
                LastAppliedEventVersion = LastAppliedEventVersion,
                UpdatedUtc = UpdatedUtc,
            };
    }
}
