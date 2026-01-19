namespace Workflow.Application.EventSourcing;

/// <summary>
/// Thrown when optimistic concurrency checks fail while appending to an event stream.
/// </summary>
public sealed class ConcurrencyException : Exception
{
    /// <summary>
    /// Creates a new instance.
    /// </summary>
    /// <param name="message">Error message.</param>
    public ConcurrencyException(string message) : base(message)
    {
    }

    /// <summary>
    /// Creates a new instance.
    /// </summary>
    /// <param name="message">Error message.</param>
    /// <param name="innerException">Inner exception.</param>
    public ConcurrencyException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
