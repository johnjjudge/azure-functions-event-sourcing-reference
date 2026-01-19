# Infrastructure (Bicep)

This folder contains a complete Bicep deployment for the reference implementation.

## What gets deployed

* Storage Account (used by Functions runtime + **Table Storage** for intake work items)
* Azure Cosmos DB for NoSQL (serverless) with:
  * `events` container (event stream)
  * `requests` container (projection)
  * `idempotency` container (at-least-once + idempotent handlers)
* Event Grid **custom topic** (CloudEvents 1.0)
* Event Grid **subscriptions** delivering to the event-driven Azure Functions
* Function App (Linux Consumption) with **System-Assigned Managed Identity**
* Application Insights (optional)
* Permission wiring:
  * Storage Table Data Contributor role on the Storage Account
  * EventGrid Data Sender role on the Event Grid Topic
  * Cosmos DB **data plane** role assignment (Built-in Data Contributor)

> Note: the external service is deployed separately. The Function App is configured with
> `ExternalService__BaseUrl` + `ExternalService__Audience` as parameters.

## Deploy

1) Create a resource group.

2) Deploy the template:

```bash
az deployment group create \
  --resource-group <rg> \
  --template-file infra/main.bicep \
  --parameters infra/dev.bicepparam
```

3) Publish the Functions app code (zip deploy or Functions Core Tools). Example:

```bash
dotnet publish src/Workflow.Functions/Workflow.Functions.csproj -c Release -o out
cd out
zip -r ../app.zip .
az functionapp deployment source config-zip \
  --resource-group <rg> \
  --name <functionAppNameFromOutputs> \
  --src ../app.zip
```

## Event Grid subscription mapping

The template creates subscriptions:

* `workflow.request.discovered.v1` -> `PrepareSubmission`
* `workflow.submission.prepared.v1` -> `SubmitJob`
* `workflow.job.pollrequested.v1` -> `PollExternalJob`
* `workflow.job.terminal.v1` -> `CompleteRequest`

The timer functions run on schedules via these app settings:

* `DiscoverSchedule`
* `PollSchedule`

## References

* Event Grid subscription destination (Azure Function) supports `endpointType: AzureFunction` with a `resourceId`.
* Azure built-in role IDs used by this template:
  * Storage Table Data Contributor: `0a9a7e1f-b9d0-4cc4-a60d-0319b160aaa3`
  * EventGrid Data Sender: `d5a91429-5739-47e2-a06b-3470a27159e7`
* Cosmos DB for NoSQL built-in data plane role IDs:
  * Cosmos DB Built-in Data Contributor: `00000000-0000-0000-0000-000000000002`
