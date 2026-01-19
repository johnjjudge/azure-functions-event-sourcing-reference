namespace Workflow.Application.Contracts;

/// <summary>
/// Standardizes the CloudEvent subject field for this workflow.
/// </summary>
public static class Subjects
{
    /// <summary>
    /// Builds a subject for events that relate to a specific request.
    /// </summary>
    public static string ForRequest(string requestId) => $"/requests/{requestId}";
}
