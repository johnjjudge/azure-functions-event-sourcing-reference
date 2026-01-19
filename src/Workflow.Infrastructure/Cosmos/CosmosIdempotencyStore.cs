using System.Net;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;
using Workflow.Application.Abstractions;
using Workflow.Infrastructure.Configuration;

namespace Workflow.Infrastructure.Cosmos;

/// <summary>
/// Cosmos DB implementation of <see cref="IIdempotencyStore"/>.
/// </summary>
/// <remarks>
/// Documents are stored in a container partitioned by <c>handler</c> and keyed by <c>id = eventId</c>.
/// A lightweight leasing model prevents duplicate concurrent processing without permanently blocking retries.
/// </remarks>
internal sealed class CosmosIdempotencyStore : IIdempotencyStore
{
    private const string StatusInProgress = "InProgress";
    private const string StatusCompleted = "Completed";

    private readonly Container _container;

    /// <summary>
    /// Creates a new instance.
    /// </summary>
    public CosmosIdempotencyStore(CosmosClient client, IOptions<CosmosDbOptions> options)
    {
        var o = options.Value;
        _container = client.GetContainer(o.DatabaseName, o.IdempotencyContainerName);
    }

    /// <inheritdoc />
    public async Task<bool> TryBeginAsync(
        string handlerName,
        string eventId,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(handlerName)) throw new ArgumentException("Handler name is required.", nameof(handlerName));
        if (string.IsNullOrWhiteSpace(eventId)) throw new ArgumentException("Event id is required.", nameof(eventId));
        if (leaseDuration <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(leaseDuration));

        var now = DateTimeOffset.UtcNow;
        var leaseUntil = now.Add(leaseDuration);
        var pk = new PartitionKey(handlerName);

        var doc = new CosmosIdempotencyDocument
        {
            Id = eventId,
            Handler = handlerName,
            Status = StatusInProgress,
            LeaseUntilUtc = leaseUntil,
            UpdatedUtc = now,
        };

        try
        {
            await _container.CreateItemAsync(doc, pk, cancellationToken: cancellationToken);
            return true;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
        {
            // Existing doc: if completed, do not process. If lease expired, attempt takeover.
        }

        ItemResponse<CosmosIdempotencyDocument> existingResponse;
        try
        {
            existingResponse = await _container.ReadItemAsync<CosmosIdempotencyDocument>(eventId, pk, cancellationToken: cancellationToken);
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            // Rare race: the doc was deleted between conflict and read. Retry by creating.
            await _container.CreateItemAsync(doc, pk, cancellationToken: cancellationToken);
            return true;
        }

        var existing = existingResponse.Resource;
        if (string.Equals(existing.Status, StatusCompleted, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (existing.LeaseUntilUtc > now)
        {
            // Another invocation currently owns the lease.
            return false;
        }

        // Attempt takeover using etag.
        var takeover = existing with
        {
            Status = StatusInProgress,
            LeaseUntilUtc = leaseUntil,
            UpdatedUtc = now,
        };

        try
        {
            await _container.ReplaceItemAsync(
                takeover,
                eventId,
                pk,
                new ItemRequestOptions { IfMatchEtag = existingResponse.ETag },
                cancellationToken);

            return true;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.PreconditionFailed)
        {
            // Lost the race.
            return false;
        }
    }

    /// <inheritdoc />
    public async Task MarkCompletedAsync(string handlerName, string eventId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(handlerName)) throw new ArgumentException("Handler name is required.", nameof(handlerName));
        if (string.IsNullOrWhiteSpace(eventId)) throw new ArgumentException("Event id is required.", nameof(eventId));

        var pk = new PartitionKey(handlerName);
        var now = DateTimeOffset.UtcNow;

        try
        {
            await _container.PatchItemAsync<CosmosIdempotencyDocument>(
                id: eventId,
                partitionKey: pk,
                patchOperations: new[]
                {
                    PatchOperation.Replace("/status", StatusCompleted),
                    PatchOperation.Replace("/updatedUtc", now),
                    // Keep the property present so the document schema remains stable.
                    PatchOperation.Replace("/leaseUntilUtc", now),
                },
                cancellationToken: cancellationToken);
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            // If completion is attempted without a begin marker, write a completed marker.
            var doc = new CosmosIdempotencyDocument
            {
                Id = eventId,
                Handler = handlerName,
                Status = StatusCompleted,
                LeaseUntilUtc = now,
                UpdatedUtc = now,
            };

            await _container.UpsertItemAsync(doc, pk, cancellationToken: cancellationToken);
        }
    }

    private sealed record CosmosIdempotencyDocument
    {
        public required string Id { get; init; }
        public required string Handler { get; init; }
        public required string Status { get; init; }
        public required DateTimeOffset LeaseUntilUtc { get; init; }
        public required DateTimeOffset UpdatedUtc { get; init; }
    }
}
