using System.Net.Http.Headers;
using System.Net.Http.Json;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Workflow.Application.Abstractions;
using Workflow.Domain.Model;

namespace Workflow.Infrastructure.ExternalService;

/// <summary>
/// HTTP client implementation of <see cref="IExternalServiceClient" />.
/// </summary>
/// <remarks>
/// In Azure, this client uses <see cref="DefaultAzureCredential" />
/// (which will resolve to Managed Identity for the Function App) to acquire
/// an access token for the external service audience.
/// </remarks>
public sealed class ExternalServiceClient : IExternalServiceClient
{
    private readonly HttpClient _http;
    private readonly ExternalServiceOptions _options;
    private readonly TokenCredential _credential;
    private readonly ILogger<ExternalServiceClient> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ExternalServiceClient" /> class.
    /// </summary>
    public ExternalServiceClient(
        HttpClient http,
        IOptions<ExternalServiceOptions> options,
        ILogger<ExternalServiceClient> logger)
        : this(http, options, new DefaultAzureCredential(), logger)
    {
    }

    /// <summary>
    /// Internal constructor for testing.
    /// </summary>
    internal ExternalServiceClient(
        HttpClient http,
        IOptions<ExternalServiceOptions> options,
        TokenCredential credential,
        ILogger<ExternalServiceClient> logger)
    {
        _http = http;
        _options = options.Value;
        _credential = credential;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<(ExternalJobId JobId, ExternalJobStatus Status)> CreateJobAsync(
        RequestId requestId,
        int attempt,
        CancellationToken cancellationToken)
    {
        await AttachAuthHeaderAsync(cancellationToken).ConfigureAwait(false);

        var dto = new CreateJobRequestDto(requestId.Value, attempt, Payload: null);

        using var response = await _http.PostAsJsonAsync("/jobs", dto, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<CreateJobResponseDto>(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (body is null)
        {
            throw new InvalidOperationException("External service returned an empty response.");
        }

        return (new ExternalJobId(body.JobId), ParseStatus(body.Status));
    }

    /// <inheritdoc />
    public async Task<ExternalJobStatus> GetStatusAsync(ExternalJobId jobId, CancellationToken cancellationToken)
    {
        await AttachAuthHeaderAsync(cancellationToken).ConfigureAwait(false);

        using var response = await _http.GetAsync($"/jobs/{Uri.EscapeDataString(jobId.Value)}", cancellationToken)
            .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<GetJobStatusResponseDto>(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (body is null)
        {
            throw new InvalidOperationException("External service returned an empty response.");
        }

        return ParseStatus(body.Status);
    }

    private async Task AttachAuthHeaderAsync(CancellationToken cancellationToken)
    {
        if (!_options.RequireAuth)
        {
            // Local dev / unauthenticated mode.
            _http.DefaultRequestHeaders.Authorization = null;
            return;
        }

        if (string.IsNullOrWhiteSpace(_options.Audience))
        {
            throw new InvalidOperationException("ExternalService:Audience must be configured when RequireAuth is true.");
        }

        var scope = _options.Audience.EndsWith("/.default", StringComparison.Ordinal)
            ? _options.Audience
            : $"{_options.Audience}/.default";

        var token = await _credential.GetTokenAsync(new TokenRequestContext(new[] { scope }), cancellationToken)
            .ConfigureAwait(false);

        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);

        _logger.LogDebug("Attached access token for scope {Scope}.", scope);
    }

    private static ExternalJobStatus ParseStatus(string wireValue)
    {
        // Wire values intentionally match enum member names.
        if (Enum.TryParse<ExternalJobStatus>(wireValue, ignoreCase: false, out var status))
        {
            return status;
        }

        throw new InvalidOperationException($"Unknown external job status '{wireValue}'.");
    }
}
