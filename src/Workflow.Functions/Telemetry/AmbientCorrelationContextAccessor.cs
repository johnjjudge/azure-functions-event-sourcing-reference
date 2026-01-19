using System.Threading;
using Workflow.Application.Telemetry;

namespace Workflow.Functions.Telemetry;

/// <summary>
/// Stores the current <see cref="CorrelationContext"/> in an <see cref="AsyncLocal{T}"/>
/// so it flows naturally through async calls within a single function invocation.
/// </summary>
public sealed class AmbientCorrelationContextAccessor : ICorrelationContextAccessor
{
    private static readonly AsyncLocal<CorrelationContext?> CurrentContext = new();

    /// <inheritdoc />
    public CorrelationContext? Current
    {
        get => CurrentContext.Value;
        set => CurrentContext.Value = value;
    }
}
