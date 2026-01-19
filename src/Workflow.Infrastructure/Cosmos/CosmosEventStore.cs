using System.Net;
using System.Text.Json;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;
using Workflow.Application.Abstractions;
using Workflow.Application.EventSourcing;
using Workflow.Domain.ValueObjects;
using Workflow.Infrastructure.Configuration;

namespace Workflow.Infrastructure.Cosmos;

/// <summary>
/// Cosmos DB implementation of <see cref="IEventStore"/>.
/// </summary>
/// <remarks>
/// Events are stored in a single container partitioned by <c>aggregateId</c>. Each partition contains:
/// <list type="bullet">
/// <item><description>A stream metadata document (<c>id = __stream</c>) storing the current version.</description></item>
/// <item><description>Event documents (<c>docType = event</c>) containing the payload and metadata.</description></item>
/// </list>
/// Stream updates use a transactional batch to ensure the metadata version and appended events are written atomically.
/// </remarks>
internal sealed class CosmosEventStore : IEventStore
{
    private const string StreamDocumentId = "__stream";
    private const string DocTypeStream = "stream";
    private const string DocTypeEvent = "event";

    private readonly Container _container;
    private readonly JsonSerializerOptions _json;

    /// <summary>
    /// Creates a new instance.
    /// </summary>
    public CosmosEventStore(CosmosClient client, IOptions<CosmosDbOptions> options)
    {
        var o = options.Value;
        _container = client.GetContainer(o.DatabaseName, o.EventsContainerName);
        _json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
    }

    /// <inheritdoc />
    public async Task<int> AppendAsync(
        RequestId aggregateId,
        IReadOnlyCollection<EventToAppend> events,
        int? expectedVersion,
        CancellationToken cancellationToken)
    {
        if (events is null) throw new ArgumentNullException(nameof(events));
        if (events.Count == 0) throw new ArgumentException("At least one event is required.", nameof(events));

        var pk = new PartitionKey(aggregateId.Value);

        // Load the stream metadata (version + etag).
        CosmosStreamDocument stream;
        string? etag;
        try
        {
            var response = await _container.ReadItemAsync<CosmosStreamDocument>(StreamDocumentId, pk, cancellationToken: cancellationToken);
            stream = response.Resource;
            etag = response.ETag;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            stream = new CosmosStreamDocument
            {
                Id = StreamDocumentId,
                AggregateId = aggregateId.Value,
                DocType = DocTypeStream,
                Version = 0,
            };
            etag = null;
        }

        if (expectedVersion is not null && expectedVersion.Value != stream.Version)
        {
            throw new ConcurrencyException($"Expected version {expectedVersion.Value} but stream is at version {stream.Version}.");
        }

        var nextVersion = stream.Version;
        var now = DateTimeOffset.UtcNow;

        // Build event docs with monotonic versions.
        var eventDocs = new List<CosmosEventDocument>(events.Count);
        foreach (var e in events)
        {
            nextVersion++;
            var json = JsonSerializer.SerializeToElement(e.Data, _json);
            eventDocs.Add(new CosmosEventDocument
            {
                Id = e.EventId,
                AggregateId = aggregateId.Value,
                DocType = DocTypeEvent,
                Version = nextVersion,
                EventType = e.EventType,
                OccurredUtc = e.OccurredUtc,
                InsertedUtc = now,
                Data = json,
                CorrelationId = e.CorrelationId,
                CausationId = e.CausationId,
            });
        }

        // Update stream version.
        var updatedStream = stream with { Version = nextVersion };

        var batch = _container.CreateTransactionalBatch(pk);

        if (etag is null)
        {
            // First writer wins. If another writer creates the stream concurrently, this will conflict.
            batch.CreateItem(updatedStream);
        }
        else
        {
            batch.ReplaceItem(StreamDocumentId, updatedStream, new TransactionalBatchItemRequestOptions { IfMatchEtag = etag });
        }

        foreach (var doc in eventDocs)
        {
            // Deterministic event ids + create semantics provide natural de-dupe.
            batch.CreateItem(doc);
        }

        TransactionalBatchResponse result;
        try
        {
            result = await batch.ExecuteAsync(cancellationToken);
        }
        catch (CosmosException ex)
        {
            throw new ConcurrencyException("Failed to append events due to a Cosmos error.", ex);
        }

        if (!result.IsSuccessStatusCode)
        {
            // 409 on create stream or 412 on etag mismatch should be treated as concurrency failures.
            throw new ConcurrencyException($"Failed to append events. Cosmos status: {(int)result.StatusCode} ({result.StatusCode}).");
        }

        return nextVersion;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<StoredEvent>> ReadStreamAsync(RequestId aggregateId, CancellationToken cancellationToken)
    {
        var pk = new PartitionKey(aggregateId.Value);

        var query = new QueryDefinition(
                "SELECT c.id, c.eventType, c.occurredUtc, c.data, c.correlationId, c.causationId, c.version " +
                "FROM c WHERE c.aggregateId = @id AND c.docType = @docType ORDER BY c.version ASC")
            .WithParameter("@id", aggregateId.Value)
            .WithParameter("@docType", DocTypeEvent);

        var iterator = _container.GetItemQueryIterator<CosmosEventProjection>(
            query,
            requestOptions: new QueryRequestOptions { PartitionKey = pk });

        var list = new List<StoredEvent>();
        while (iterator.HasMoreResults)
        {
            var page = await iterator.ReadNextAsync(cancellationToken);
            foreach (var item in page)
            {
                list.Add(new StoredEvent(
                    EventId: item.Id,
                    EventType: item.EventType,
                    OccurredUtc: item.OccurredUtc,
                    Data: item.Data,
                    CorrelationId: item.CorrelationId,
                    CausationId: item.CausationId,
                    Version: item.Version));
            }
        }

        return list;
    }

    private sealed record CosmosStreamDocument
    {
        public required string Id { get; init; }
        public required string AggregateId { get; init; }
        public required string DocType { get; init; }
        public required int Version { get; init; }
    }

    private sealed class CosmosEventDocument
    {
        public required string Id { get; init; }
        public required string AggregateId { get; init; }
        public required string DocType { get; init; }
        public required int Version { get; init; }
        public required string EventType { get; init; }
        public required DateTimeOffset OccurredUtc { get; init; }
        public required DateTimeOffset InsertedUtc { get; init; }
        public required JsonElement Data { get; init; }
        public string? CorrelationId { get; init; }
        public string? CausationId { get; init; }
    }

    private sealed class CosmosEventProjection
    {
        public required string Id { get; init; }
        public required string EventType { get; init; }
        public required DateTimeOffset OccurredUtc { get; init; }
        public required JsonElement Data { get; init; }
        public string? CorrelationId { get; init; }
        public string? CausationId { get; init; }
        public required int Version { get; init; }
    }
}
