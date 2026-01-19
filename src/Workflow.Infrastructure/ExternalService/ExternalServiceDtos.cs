using System.Text.Json.Serialization;

namespace Workflow.Infrastructure.ExternalService;

/// <summary>
/// Request body for POST /jobs.
/// </summary>
public sealed record CreateJobRequestDto(
    [property: JsonPropertyName("requestId")] string RequestId,
    [property: JsonPropertyName("attempt")] int Attempt,
    [property: JsonPropertyName("payload")] string? Payload);

/// <summary>
/// Response body from POST /jobs.
/// </summary>
public sealed record CreateJobResponseDto(
    [property: JsonPropertyName("jobId")] string JobId,
    [property: JsonPropertyName("status")] string Status);

/// <summary>
/// Response body from GET /jobs/{id}.
/// </summary>
public sealed record GetJobStatusResponseDto(
    [property: JsonPropertyName("jobId")] string JobId,
    [property: JsonPropertyName("requestId")] string RequestId,
    [property: JsonPropertyName("attempt")] int Attempt,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("message")] string? Message);
