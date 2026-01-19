using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Azure.Cosmos;
using Workflow.Application.Abstractions;
using Workflow.Application.EventSourcing;
using Workflow.Application.Configuration;
using Workflow.Application.Projections;
using Workflow.Application.Telemetry;
using Workflow.Application.UseCases.Discover;
using Workflow.Application.UseCases.PrepareSubmission;
using Workflow.Application.UseCases.SubmitJob;
using Workflow.Application.UseCases.Polling;
using Workflow.Application.UseCases.Completion;
using Workflow.Infrastructure.Configuration;
using Workflow.Infrastructure.Cosmos;
using Workflow.Infrastructure.Eventing;
using Workflow.Infrastructure.Tables;
using Workflow.Infrastructure.ExternalService;

namespace Workflow.Infrastructure.DependencyInjection;

/// <summary>
/// Registers infrastructure adapters.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers infrastructure services for the workflow.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="configuration">Application configuration.</param>
    public static IServiceCollection AddWorkflowInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddOptions<WorkflowOptions>()
            .Bind(configuration.GetSection("Workflow"))
            .ValidateOnStart();

        services
            .AddOptions<EventGridOptions>()
            .Bind(configuration.GetSection("EventGrid"))
            .Validate(o => o.TopicEndpoint is not null, "EventGrid:TopicEndpoint is required.")
            .ValidateOnStart();

        // NOTE: ICorrelationContextAccessor is registered by the host (Functions) layer,
        // because the host is responsible for reading correlation values from incoming events.

        services.AddSingleton<IEventPublisher, EventGridEventPublisher>();

        // Cross-cutting infrastructure.
        services.AddSingleton<IClock, SystemClock>();

        services
            .AddOptions<TableStorageOptions>()
            .Bind(configuration.GetSection("Table"))
            .Validate(o => !string.IsNullOrWhiteSpace(o.AccountUri) || !string.IsNullOrWhiteSpace(o.ConnectionString), "Table:AccountUri or Table:ConnectionString is required.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.TableName), "Table:TableName is required.")
            .ValidateOnStart();

        services.AddSingleton(sp =>
        {
            var factory = new TableClientFactory(sp.GetRequiredService<IOptions<TableStorageOptions>>());
            return factory.CreateClient();
        });

        services.AddSingleton<ITableIntakeRepository, TableIntakeRepository>();

        services
            .AddOptions<CosmosDbOptions>()
            .Bind(configuration.GetSection("Cosmos"))
            .Validate(o => !string.IsNullOrWhiteSpace(o.ConnectionString) || !string.IsNullOrWhiteSpace(o.AccountEndpoint), "Cosmos:ConnectionString or Cosmos:AccountEndpoint is required.")
            .ValidateOnStart();

        services.AddSingleton<CosmosClient>(sp =>
        {
            var factory = new CosmosClientFactory(sp.GetRequiredService<IOptions<CosmosDbOptions>>());
            return factory.CreateClient();
        });

        services.AddSingleton<IEventStore, CosmosEventStore>();
        services.AddSingleton<IIdempotencyStore, CosmosIdempotencyStore>();
        services.AddSingleton<IEventIdFactory, DeterministicEventIdFactory>();
        services.AddSingleton<IRequestProjectionRepository, CosmosRequestProjectionRepository>();
        services.AddSingleton<RequestProjectionReducer>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<WorkflowOptions>>().Value;
            return new RequestProjectionReducer(opts);
        });
        services.AddSingleton<RequestProjectionUpdater>();

        // External service client.
        services
            .AddOptions<ExternalServiceOptions>()
            .Bind(configuration.GetSection("ExternalService"))
            .Validate(o => !string.IsNullOrWhiteSpace(o.BaseUrl), "ExternalService:BaseUrl is required.")
            .Validate(o => !o.RequireAuth || !string.IsNullOrWhiteSpace(o.Audience), "ExternalService:Audience is required when RequireAuth is true.")
            .ValidateOnStart();

        services
            .AddHttpClient<IExternalServiceClient, ExternalServiceClient>((sp, http) =>
            {
                var opts = sp.GetRequiredService<IOptions<ExternalServiceOptions>>().Value;
                http.BaseAddress = new Uri(opts.BaseUrl, UriKind.Absolute);
            });

        // Application use cases.
        services.AddSingleton<DiscoverUnprocessedRequestsHandler>();
        services.AddSingleton<PrepareSubmissionHandler>();
        services.AddSingleton<SubmitJobHandler>();
        services.AddSingleton<ScheduleDuePollsHandler>();
        services.AddSingleton<PollExternalJobHandler>();
        services.AddSingleton<CompleteRequestHandler>();

        return services;
    }
}
