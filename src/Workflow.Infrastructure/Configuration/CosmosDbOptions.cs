namespace Workflow.Infrastructure.Configuration;

/// <summary>
/// Cosmos DB configuration for event storage and projections.
/// </summary>
public sealed class CosmosDbOptions
{
    /// <summary>
    /// Optional connection string. If provided, it is preferred over token-based auth.
    /// </summary>
    public string? ConnectionString { get; init; }

    /// <summary>
    /// Cosmos account endpoint URI (required when using token-based authentication).
    /// </summary>
    public string? AccountEndpoint { get; init; }

    /// <summary>
    /// Database name.
    /// </summary>
    public string DatabaseName { get; init; } = "workflow";

    /// <summary>
    /// Container used for append-only events (partition key: /aggregateId).
    /// </summary>
    public string EventsContainerName { get; init; } = "events";

    /// <summary>
    /// Container used for query-optimized projections (partition key: /requestId).
    /// </summary>
    public string ProjectionsContainerName { get; init; } = "requests";

    /// <summary>
    /// Container used to store idempotency markers (partition key: /handler).
    /// </summary>
    public string IdempotencyContainerName { get; init; } = "idempotency";
}
