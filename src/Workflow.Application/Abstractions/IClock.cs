namespace Workflow.Application.Abstractions;

/// <summary>
/// Provides access to the current time.
/// </summary>
/// <remarks>
/// Abstracting time makes domain and application logic testable and helps avoid hidden
/// dependencies on <see cref="DateTimeOffset.UtcNow"/>.
/// </remarks>
public interface IClock
{
    /// <summary>
    /// Gets the current UTC time.
    /// </summary>
    DateTimeOffset UtcNow { get; }
}
