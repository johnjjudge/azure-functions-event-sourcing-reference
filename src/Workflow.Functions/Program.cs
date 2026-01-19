using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Workflow.Application.Telemetry;
using Workflow.Functions.Telemetry;
using Workflow.Infrastructure.DependencyInjection;

/// <summary>
/// Azure Functions isolated worker host.
/// </summary>
public static class Program
{
    /// <summary>
    /// Application entry point.
    /// </summary>
    public static void Main()
    {
        var host = new HostBuilder()
            .ConfigureFunctionsWorkerDefaults()
            .ConfigureServices((context, services) =>
            {
                // Host-layer registrations.
                services.AddSingleton<ICorrelationContextAccessor, AmbientCorrelationContextAccessor>();

                // Infrastructure adapters.
                services.AddWorkflowInfrastructure(context.Configuration);
            })
            .Build();

        host.Run();
    }
}
