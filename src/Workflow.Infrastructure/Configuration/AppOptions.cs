namespace Workflow.Infrastructure.Configuration;

/// <summary>
/// Strongly-typed configuration for the workflow.
/// </summary>
public sealed class AppOptions
{
    /// <summary>
    /// Poll interval used by the timer poller.
    /// </summary>
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Maximum submit attempts for transient failures.
    /// </summary>
    public int MaxSubmitAttempts { get; init; } = 3;
}
