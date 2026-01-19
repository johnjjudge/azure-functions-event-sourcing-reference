# Local development

This repo is easiest to validate end-to-end in Azure because **Event Grid triggers** require a real Event Grid topic + subscription.

That said, you have two solid loops:

1) **Azure resources + run Functions locally** (recommended)
2) **Component-level local dev** (unit tests + run the mock service)

## Prerequisites

- .NET 8 SDK
- Azure Functions Core Tools v4
- Azure CLI (`az`)
- Access to an Azure subscription (for Cosmos DB + Event Grid)

## Auth model used in this repo

The Functions app uses `DefaultAzureCredential` for Azure services.

- **Locally**: sign in with Azure CLI:

  ```bash
  az login
  ```

  `DefaultAzureCredential` will typically pick up your CLI identity.

- **In Azure**: the Function App uses its **system-assigned managed identity**.

The included mock external service supports:

- **Local bypass** (default) — no bearer token required
- **AAD JWT validation** — to demonstrate a real Managed Identity call path

## Loop 1 (recommended): Azure resources + local Functions host

### 1) Provision the Azure resources

Follow `docs/DEPLOY_AZURE.md` to deploy the Bicep template.

You’ll end up with:

- Cosmos DB (events + projections + idempotency)
- Event Grid custom topic and subscriptions
- Storage account (with the intake table)

### 2) Configure local settings

Copy the example settings:

```bash
cp src/Workflow.Functions/local.settings.json.example src/Workflow.Functions/local.settings.json
```

Update these values in `local.settings.json`:

- `EventGrid:TopicEndpoint`
- `Cosmos:AccountEndpoint` **or** `Cosmos:ConnectionString`
- `Table:AccountUri` **or** `Table:ConnectionString`

For local dev, it’s normal to use connection strings.

### 3) Run the external service mock

```bash
dotnet run --project src/ExternalService.Mock
```

By default, the mock runs in bypass mode (no token required).

Ensure your Functions `local.settings.json` has:

- `ExternalService:BaseUrl` pointing to the mock service
- `ExternalService:RequireAuth=false`

### 4) Start the Functions host

```bash
cd src/Workflow.Functions
func start
```

### 5) Seed the intake table

Add a few entities to the intake table (`Intake` by default):

- `PartitionKey`: any non-empty string
- `RowKey`: any non-empty string
- `Status`: `Unprocessed`
- `LeaseUntilUtc`: optional (if present, set it to a time in the past)

The simplest way is using **Azure Storage Explorer**.

Once seeded, the timer functions will:

- claim items (`Unprocessed` → `InProgress` with a lease)
- emit events into Event Grid
- append events into Cosmos
- build projections and schedule polls

### 6) Observe the system

- Function logs: the local host console
- Cosmos DB:
  - `events` container shows event documents per request
  - `requests` container shows projections (read models)
- Table Storage: rows transition to `Pass` / `Fail`

## Loop 2: Component-level local dev

If you don’t want to provision Azure resources yet:

1) Run unit tests:

```bash
dotnet test
```

2) Run the external service mock and call it directly:

```bash
dotnet run --project src/ExternalService.Mock
```

This won’t exercise Event Grid triggers, but it’s useful for iterating on:

- external service status progression
- domain rules and projection reducer
- deterministic id generation

## Enabling AAD auth for the mock service (optional)

The mock service can validate JWTs to demonstrate a Managed Identity call.

At a high level you’ll:

1) configure the mock service with an **App ID URI** (audience)
2) configure the Functions app with `ExternalService:Audience`
3) (when deployed) grant the Function App identity access to that API

The exact Entra configuration varies by environment, so this repo keeps it optional.

For most portfolio/demo use, bypass mode is sufficient.
