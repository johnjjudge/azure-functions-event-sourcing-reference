using System.Text.Json;
using Azure.Core;
using Azure.Messaging;
using Workflow.Application.Telemetry;

namespace Workflow.Functions.Telemetry;

/// <summary>
/// Helper methods for working with CloudEvents in Azure Functions.
/// </summary>
public static class CloudEventHelpers
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Extracts correlation and causation ids from CloudEvent extension attributes.
    /// </summary>
    public static CorrelationContext? ExtractCorrelation(CloudEvent cloudEvent)
    {
        if (cloudEvent.ExtensionAttributes is null)
        {
            return null;
        }

        if (!cloudEvent.ExtensionAttributes.TryGetValue("correlationId", out var correlationObj) || correlationObj is not string correlationId)
        {
            return null;
        }

        if (!cloudEvent.ExtensionAttributes.TryGetValue("causationId", out var causationObj) || causationObj is not string causationId)
        {
            causationId = cloudEvent.Id ?? correlationId;
        }

        return new CorrelationContext(correlationId, causationId);
    }

    /// <summary>
    /// Deserializes the CloudEvent data into a strongly typed payload.
    /// </summary>
    public static T DeserializeData<T>(CloudEvent cloudEvent)
    {
        if (cloudEvent.Data is null)
        {
            throw new InvalidOperationException("CloudEvent.Data is null.");
        }

        var result = cloudEvent.Data.ToObjectFromJson<T>(SerializerOptions);
        if (result is null)
        {
            throw new InvalidOperationException($"Failed to deserialize {typeof(T).Name} from CloudEvent data.");
        }

        return result;
    }
}
