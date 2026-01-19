using Workflow.Application.Abstractions;

namespace Workflow.Infrastructure.Configuration;

/// <summary>
/// Production implementation of <see cref="IClock"/> backed by <see cref="DateTimeOffset.UtcNow"/>.
/// </summary>
public sealed class SystemClock : IClock
{
    /// <inheritdoc />
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
