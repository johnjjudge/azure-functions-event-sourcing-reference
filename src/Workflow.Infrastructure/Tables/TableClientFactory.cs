using Azure.Data.Tables;
using Azure.Identity;
using Microsoft.Extensions.Options;
using Workflow.Infrastructure.Configuration;

namespace Workflow.Infrastructure.Tables;

/// <summary>
/// Creates an <see cref="TableClient"/> for the configured intake table.
/// </summary>
public sealed class TableClientFactory
{
    private readonly IOptions<TableStorageOptions> _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="TableClientFactory"/> class.
    /// </summary>
    /// <param name="options">Table storage options.</param>
    public TableClientFactory(IOptions<TableStorageOptions> options)
    {
        _options = options;
    }

    /// <summary>
    /// Creates a table client for the configured table.
    /// </summary>
    public TableClient CreateClient()
    {
        var o = _options.Value;

        if (!string.IsNullOrWhiteSpace(o.AccountUri))
        {
            var credential = new DefaultAzureCredential();
            var serviceClient = new TableServiceClient(new Uri(o.AccountUri), credential);
            return serviceClient.GetTableClient(o.TableName);
        }

        if (!string.IsNullOrWhiteSpace(o.ConnectionString))
        {
            return new TableClient(o.ConnectionString, o.TableName);
        }

        throw new InvalidOperationException("Table:AccountUri or Table:ConnectionString must be provided.");
    }
}
