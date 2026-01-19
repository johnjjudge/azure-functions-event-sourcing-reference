using System.Security.Cryptography;
using System.Text;
using Workflow.Application.Abstractions;

namespace Workflow.Application.EventSourcing;

/// <summary>
/// Creates deterministic, URL-safe event ids using a SHA-256 hash.
/// </summary>
public sealed class DeterministicEventIdFactory : IEventIdFactory
{
    /// <inheritdoc />
    public string CreateDeterministic(string aggregateId, string eventType, string? correlationId, string? causationId, string? discriminator = null)
    {
        if (string.IsNullOrWhiteSpace(aggregateId)) throw new ArgumentException("Aggregate id is required.", nameof(aggregateId));
        if (string.IsNullOrWhiteSpace(eventType)) throw new ArgumentException("Event type is required.", nameof(eventType));

        // Concatenate stable inputs. Null values are normalized.
        var seed = string.Join('|',
            aggregateId.Trim(),
            eventType.Trim(),
            (correlationId ?? string.Empty).Trim(),
            (causationId ?? string.Empty).Trim(),
            (discriminator ?? string.Empty).Trim());

        var bytes = Encoding.UTF8.GetBytes(seed);
        var hash = SHA256.HashData(bytes);

        // Base64Url: compact and safe for identifiers.
        var b64 = Convert.ToBase64String(hash)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

        return b64;
    }
}
