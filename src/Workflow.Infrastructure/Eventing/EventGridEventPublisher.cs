using Azure;
using Azure.Identity;
using Azure.Messaging;
using Azure.Messaging.EventGrid;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Workflow.Application.Abstractions;
using Workflow.Application.Telemetry;
using Workflow.Infrastructure.Configuration;

namespace Workflow.Infrastructure.Eventing;

/// <summary>
/// Publishes CloudEvents to an Azure Event Grid custom topic.
/// </summary>
/// <remarks>
/// This publisher supports both shared access key authentication (topic key)
/// and Entra ID authentication (managed identity / developer credentials via <see cref="DefaultAzureCredential"/>).
/// </remarks>
public sealed class EventGridEventPublisher : IEventPublisher
{
    private readonly EventGridPublisherClient _client;
    private readonly EventGridOptions _options;
    private readonly ICorrelationContextAccessor _correlation;
    private readonly ILogger<EventGridEventPublisher> _logger;

    /// <summary>
    /// Creates a new instance.
    /// </summary>
    public EventGridEventPublisher(
        IOptions<EventGridOptions> options,
        ICorrelationContextAccessor correlation,
        ILogger<EventGridEventPublisher> logger)
    {
        _options = options.Value;
        _correlation = correlation;
        _logger = logger;

        // Prefer a topic key when supplied; otherwise fall back to Entra ID.
        _client = string.IsNullOrWhiteSpace(_options.TopicKey)
            ? new EventGridPublisherClient(_options.TopicEndpoint, new DefaultAzureCredential())
            : new EventGridPublisherClient(_options.TopicEndpoint, new AzureKeyCredential(_options.TopicKey));
    }

    /// <inheritdoc />
    public async Task PublishAsync<T>(
        string eventType,
        string subject,
        string eventId,
        T data,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(eventType)) throw new ArgumentException("Event type is required.", nameof(eventType));
        if (string.IsNullOrWhiteSpace(subject)) throw new ArgumentException("Subject is required.", nameof(subject));
        if (string.IsNullOrWhiteSpace(eventId)) throw new ArgumentException("Event id is required.", nameof(eventId));

        var cloudEvent = new CloudEvent(_options.Source, eventType, data)
        {
            Id = eventId,
            Subject = subject,
            Time = DateTimeOffset.UtcNow,
            DataContentType = "application/json",
        };

        StampCorrelation(cloudEvent);

        _logger.LogInformation(
            "Publishing CloudEvent {EventType} {EventId} Subject={Subject}",
            eventType,
            eventId,
            subject);

        await _client.SendEventAsync(cloudEvent, cancellationToken).ConfigureAwait(false);
    }

    private void StampCorrelation(CloudEvent cloudEvent)
    {
        var ctx = _correlation.Current;
        if (ctx is null)
        {
            return;
        }

        // These are CloudEvents extension attributes.
        // The names are intentionally simple and stable for demo purposes.
        cloudEvent["correlationId"] = ctx.CorrelationId;
        cloudEvent["causationId"] = ctx.CausationId;
    }
}
