namespace Workflow.Application.Telemetry;

/// <summary>
/// Provides access to the current <see cref="CorrelationContext"/> for the executing handler.
/// </summary>
/// <remarks>
/// In Functions, this is typically populated from the incoming CloudEvent headers and
/// used to stamp outgoing events.
/// </remarks>
public interface ICorrelationContextAccessor
{
    /// <summary>
    /// Gets or sets the current context. Implementations may use <see cref="AsyncLocal{T}"/>.
    /// </summary>
    CorrelationContext? Current { get; set; }
}
