using Azure.Identity;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;
using Workflow.Infrastructure.Configuration;

namespace Workflow.Infrastructure.Cosmos;

/// <summary>
/// Creates a configured <see cref="CosmosClient"/>.
/// </summary>
internal sealed class CosmosClientFactory
{
    private readonly CosmosDbOptions _options;

    /// <summary>
    /// Creates a new instance.
    /// </summary>
    /// <param name="options">Cosmos options.</param>
    public CosmosClientFactory(IOptions<CosmosDbOptions> options)
    {
        _options = options.Value;
    }

    /// <summary>
    /// Creates a Cosmos client using either a connection string (preferred for local dev) or token-based auth
    /// (preferred for managed identity in Azure).
    /// </summary>
    public CosmosClient CreateClient()
    {
        var clientOptions = new CosmosClientOptions
        {
            SerializerOptions = new CosmosSerializationOptions
            {
                PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase,
            },
        };

        if (!string.IsNullOrWhiteSpace(_options.ConnectionString))
        {
            return new CosmosClient(_options.ConnectionString, clientOptions);
        }

        if (string.IsNullOrWhiteSpace(_options.AccountEndpoint))
        {
            throw new InvalidOperationException("Cosmos:AccountEndpoint is required when Cosmos:ConnectionString is not provided.");
        }

        var credential = new DefaultAzureCredential();
        return new CosmosClient(_options.AccountEndpoint, credential, clientOptions);
    }
}
