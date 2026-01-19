namespace Workflow.Infrastructure.Configuration;

/// <summary>
/// Configuration for publishing CloudEvents to an Azure Event Grid custom topic.
/// </summary>
public sealed class EventGridOptions
{
    /// <summary>
    /// The Event Grid custom topic endpoint URI.
    /// </summary>
    /// <remarks>
    /// Example: <c>https://&lt;topic-name&gt;.&lt;region&gt;-1.eventgrid.azure.net/api/events</c>.
    /// </remarks>
    public required Uri TopicEndpoint { get; init; }

    /// <summary>
    /// Optional shared access key for the topic.
    /// </summary>
    /// <remarks>
    /// When this is not provided, the publisher will use Entra ID authentication via
    /// <see cref="Azure.Identity.DefaultAzureCredential" /> and requires the "EventGrid Data Sender" role.
    /// </remarks>
    public string? TopicKey { get; init; }

    /// <summary>
    /// CloudEvents <c>source</c> URI stamped on all outgoing events.
    /// </summary>
    /// <remarks>
    /// For a reference implementation repository, a stable URN is usually sufficient.
    /// </remarks>
    public Uri Source { get; init; } = new Uri("urn:workflow:event-sourced-functions");
}
