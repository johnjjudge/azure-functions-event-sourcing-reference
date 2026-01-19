namespace Workflow.Domain.ValueObjects;

/// <summary>
/// Identifies a job as understood by the external service.
/// </summary>
/// <param name="Value">The opaque identifier returned by the external service.</param>
public readonly record struct ExternalJobId(string Value)
{
    /// <summary>
    /// Creates an <see cref="ExternalJobId"/> from a raw identifier string.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown if <paramref name="value"/> is null, empty, or whitespace.</exception>
    public static ExternalJobId From(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("External job id must be non-empty.", nameof(value));
        }

        return new ExternalJobId(value);
    }

    /// <summary>
    /// Returns the underlying value.
    /// </summary>
    public override string ToString() => Value;
}
