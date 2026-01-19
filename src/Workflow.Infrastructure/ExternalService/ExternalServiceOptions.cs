namespace Workflow.Infrastructure.ExternalService;

/// <summary>
/// Configuration values for the external service used by this reference implementation.
/// </summary>
public sealed class ExternalServiceOptions
{
    /// <summary>
    /// The base URL of the external service (e.g., https://localhost:7075).
    /// </summary>
    public string BaseUrl { get; init; } = string.Empty;

    /// <summary>
    /// If <c>true</c>, the client will attach an Entra ID access token to requests.
    /// </summary>
    /// <remarks>
    /// When running the sample external service locally, this is typically <c>false</c>.
    /// When deployed, this should be <c>true</c> so the Functions app can authenticate
    /// using Managed Identity.
    /// </remarks>
    public bool RequireAuth { get; init; } = true;

    /// <summary>
    /// The App ID URI (audience) of the external service API.
    /// </summary>
    /// <example>api://externalservice</example>
    public string? Audience { get; init; }
}
