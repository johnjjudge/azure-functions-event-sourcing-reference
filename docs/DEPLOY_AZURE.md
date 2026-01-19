# Deploy to Azure

This repo includes a Bicep template under `infra/` that provisions the Azure resources required by the workflow.

## Prerequisites

- Azure CLI (`az`)
- A subscription where you can create: Resource Group, Storage Account, Cosmos DB for NoSQL, Event Grid custom topic, Function App
- (Optional) Azure Functions Core Tools if you prefer `func azure functionapp publish`

## 1) Deploy infrastructure

Create a resource group:

```bash
az group create --name <rg> --location <location>
```

Deploy the Bicep template:

```bash
az deployment group create \
  --resource-group <rg> \
  --template-file infra/main.bicep \
  --parameters infra/dev.bicepparam
```

The deployment outputs include:

- Function App name
- Event Grid topic endpoint
- Cosmos account endpoint
- Storage account name

## 2) Deploy the Functions code

### Option A: Zip deploy

```bash
dotnet publish src/Workflow.Functions/Workflow.Functions.csproj -c Release -o out
cd out
zip -r ../app.zip .

az functionapp deployment source config-zip \
  --resource-group <rg> \
  --name <functionAppNameFromOutputs> \
  --src ../app.zip
```

### Option B: Functions Core Tools

```bash
cd src/Workflow.Functions
func azure functionapp publish <functionAppNameFromOutputs>
```

## 3) Seed the intake table

The intake table is named `Intake` by default. Insert a few rows with:

- `PartitionKey`: any non-empty string
- `RowKey`: any non-empty string
- `Status`: `Unprocessed`
- `LeaseUntilUtc`: optional (if present, set to a past timestamp)

The simplest way is **Azure Storage Explorer**:

1) Connect to the deployed storage account
2) Navigate to Tables → `Intake`
3) Add entities

## 4) Watch the workflow run

You can observe behavior in:

- **Function logs** (Log stream or Application Insights)
- **Cosmos DB**
  - `events` container: append-only event documents per request
  - `requests` container: projection/read model
  - `idempotency` container: handler de-duplication records
- **Table Storage**
  - rows transition to `Pass` or `Fail`

## 5) External service

This repo includes a local mock external service (`src/ExternalService.Mock`). The Bicep deploys the workflow infrastructure, but does not deploy the mock service.

For a cloud deployment you have two common options:

1) Deploy your own API (App Service, Container Apps, Functions, etc.) and set:
   - `ExternalService__BaseUrl`
   - `ExternalService__Audience` (App ID URI)
   - `ExternalService__RequireAuth=true`

2) Keep the workflow pointed at a dev endpoint and run the mock service from your machine (only appropriate for non-production demos).

### Managed Identity note

The workflow uses `DefaultAzureCredential` to request tokens for:

```
{ExternalService:Audience}/.default
```

Your external service must validate Entra ID bearer tokens for the configured audience.

## Troubleshooting

- **Nothing happens**: confirm timer schedules are enabled and the intake table has `Unprocessed` rows.
- **Event handlers not firing**: verify Event Grid subscriptions exist and your Functions endpoints are healthy.
- **Cosmos authorization**: confirm the Function App identity has the Cosmos DB built-in data role assignment.

For infra details (parameters, role assignments, subscription mapping), see `infra/README.md`.
