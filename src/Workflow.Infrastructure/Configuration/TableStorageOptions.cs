namespace Workflow.Infrastructure.Configuration;

/// <summary>
/// Configuration for Azure Table Storage.
/// </summary>
public sealed class TableStorageOptions
{
    /// <summary>
    /// Table service account URI (for example, https://{account}.table.core.windows.net).
    /// When provided, the application will authenticate using Entra ID (Managed Identity, Azure CLI, etc.).
    /// </summary>
    public string? AccountUri { get; init; }

    /// <summary>
    /// Connection string for Table Storage.
    /// Prefer <see cref="AccountUri"/> + Managed Identity for deployed environments.
    /// </summary>
    public string? ConnectionString { get; init; }

    /// <summary>
    /// Intake table name.
    /// </summary>
    public string TableName { get; init; } = "Intake";
}
