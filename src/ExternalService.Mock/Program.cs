using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Identity.Web;

var builder = WebApplication.CreateBuilder(args);

// -----------------------------------------------------------------------------
// Authentication / Authorization
// -----------------------------------------------------------------------------
// This mock service supports two modes:
//  1) Auth disabled (default): simple local dev / cloning experience.
//  2) Auth enabled: validates Entra ID (Azure AD) access tokens.
//
// When deployed in Azure, the Functions app can use Managed Identity to request
// an access token for the configured audience (App ID URI) and call this API.
// -----------------------------------------------------------------------------
var useAuth = builder.Configuration.GetValue<bool>("Auth:Enabled");
if (useAuth)
{
    builder.Services
        .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddMicrosoftIdentityWebApi(builder.Configuration.GetSection("AzureAd"));

    builder.Services.AddAuthorization();
}

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

if (useAuth)
{
    app.UseAuthentication();
    app.UseAuthorization();
}

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

// -----------------------------------------------------------------------------
// In-memory job store
// -----------------------------------------------------------------------------
// This is a mock. In a real external service, job state would be persisted.
// For portfolio/demo purposes, we keep a small in-memory store and compute job
// progression deterministically from (RequestId, Attempt) + elapsed time.
// -----------------------------------------------------------------------------
var jobs = new ConcurrentDictionary<string, JobRecord>(StringComparer.Ordinal);

var jobsGroup = app.MapGroup("/jobs");
if (useAuth)
{
    jobsGroup.RequireAuthorization();
}

jobsGroup.MapPost("/", (CreateJobRequest req) =>
{
    if (string.IsNullOrWhiteSpace(req.RequestId))
    {
        return Results.BadRequest(new { error = "RequestId is required." });
    }

    if (req.Attempt <= 0)
    {
        return Results.BadRequest(new { error = "Attempt must be >= 1." });
    }

    var jobId = JobIdFactory.Create(req.RequestId, req.Attempt);

    // Idempotency: if the same (RequestId, Attempt) is submitted multiple times, return the existing job.
    // This ensures retries from the caller do not reset the job timeline.
    var record = jobs.GetOrAdd(
        jobId,
        _ => new JobRecord(jobId, req.RequestId, req.Attempt, DateTimeOffset.UtcNow));

    var status = JobStatusComputer.Compute(record);
    return Results.Ok(new CreateJobResponse(record.JobId, status));
})
.WithName("CreateJob");

jobsGroup.MapGet("/{jobId}", (string jobId) =>
{
    if (!jobs.TryGetValue(jobId, out var record))
    {
        return Results.NotFound(new { error = "Job not found." });
    }

    var status = JobStatusComputer.Compute(record);

    return Results.Ok(new GetJobStatusResponse(
        JobId: record.JobId,
        RequestId: record.RequestId,
        Attempt: record.Attempt,
        Status: status,
        Message: status == ExternalJobStatus.FailCanRetry
            ? "Transient failure. Caller may resubmit."
            : null));
})
.WithName("GetJobStatus");

app.Run();

/// <summary>
/// Request body for creating a job.
/// </summary>
/// <param name="RequestId">The workflow request identifier (PartitionKey|RowKey).</param>
/// <param name="Attempt">The submission attempt number (1-based).</param>
/// <param name="Payload">Optional opaque payload for demo purposes.</param>
public sealed record CreateJobRequest(string RequestId, int Attempt, string? Payload);

/// <summary>
/// Response returned by the create-job endpoint.
/// </summary>
/// <param name="JobId">The created job identifier.</param>
/// <param name="Status">The initial status.</param>
public sealed record CreateJobResponse(string JobId, string Status);

/// <summary>
/// Response returned by the get-job-status endpoint.
/// </summary>
/// <param name="JobId">The job identifier.</param>
/// <param name="RequestId">The workflow request identifier.</param>
/// <param name="Attempt">The attempt number.</param>
/// <param name="Status">Current status.</param>
/// <param name="Message">Optional human-readable description.</param>
public sealed record GetJobStatusResponse(
    string JobId,
    string RequestId,
    int Attempt,
    string Status,
    string? Message);

/// <summary>
/// Internal record holding job metadata.
/// </summary>
internal sealed record JobRecord(
    string JobId,
    string RequestId,
    int Attempt,
    DateTimeOffset CreatedUtc);

/// <summary>
/// Allowed external job status values.
/// </summary>
internal static class ExternalJobStatus
{
    public const string Created = "Created";
    public const string Inprogress = "Inprogress";
    public const string Pass = "Pass";
    public const string Fail = "Fail";
    public const string FailCanRetry = "FailCanRetry";
}

/// <summary>
/// Deterministic job ID generator.
/// </summary>
internal static class JobIdFactory
{
    public static string Create(string requestId, int attempt)
    {
        // Deterministic: given the same (requestId, attempt) this will generate the same job ID.
        // This is helpful for repeatable demos and makes behavior easy to reason about.
        var input = $"{requestId}:{attempt}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Base64Url(hash).Substring(0, 22);
    }

    private static string Base64Url(byte[] bytes)
        => Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}

/// <summary>
/// Computes a deterministic status progression for a job.
/// </summary>
internal static class JobStatusComputer
{
    public static string Compute(JobRecord record)
    {
        var elapsed = DateTimeOffset.UtcNow - record.CreatedUtc;

        // Time-based progression (simple demo):
        //  0-15s  => Created
        // 15-60s => Inprogress
        // 60s+   => terminal (Pass/Fail/FailCanRetry)
        if (elapsed < TimeSpan.FromSeconds(15)) return ExternalJobStatus.Created;
        if (elapsed < TimeSpan.FromSeconds(60)) return ExternalJobStatus.Inprogress;

        return TerminalOutcome(record.RequestId, record.Attempt);
    }

    private static string TerminalOutcome(string requestId, int attempt)
    {
        // Deterministic terminal classification by RequestId.
        //
        // Rules (chosen to exercise retry paths):
        //  - ~10%: always Fail (terminal)
        //  - ~10%: first attempt FailCanRetry, subsequent attempts Pass
        //  - remainder: Pass
        var bucket = StableBucket(requestId, 10);

        if (bucket == 0) return ExternalJobStatus.Fail;
        if (bucket == 1 && attempt == 1) return ExternalJobStatus.FailCanRetry;

        return ExternalJobStatus.Pass;
    }

    private static int StableBucket(string value, int modulo)
    {
        // A stable (platform-independent) hash for bucketing.
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        // Use the first 4 bytes as an unsigned int.
        var n = (uint)(bytes[0] << 24 | bytes[1] << 16 | bytes[2] << 8 | bytes[3]);
        return (int)(n % (uint)modulo);
    }
}
