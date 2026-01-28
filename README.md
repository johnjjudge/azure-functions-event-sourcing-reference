# Event-Sourced, Event-Driven Azure Functions (DDD Reference Implementation)

This repository is a reference implementation that demonstrates a realistic Azure workflow using:

- **Domain-Driven Design (DDD)** structure (Domain / Application / Infrastructure)
- **Event sourcing** with **Azure Cosmos DB for NoSQL** (append-only event store)
- A lightweight **projection** (read model) derived from events (rebuildable)
- **Azure Event Grid** (Custom Topic) using **CloudEvents 1.0** for integration events
- **Azure Functions (.NET isolated worker)** as the compute host
- **Azure Table Storage** as an intake/source of work items
- **Timer-based scheduling** for polling every 5 minutes
- **Managed Identity (Entra ID)** to call an external service (sample service included)

The “business scenario” is intentionally tiny and generic:

> Discover unprocessed work items → prepare a submission → submit to an external service → poll for completion → write back a terminal status.

## Why this repo exists

The intent is to showcase **architecture and engineering practices** that are valuable in real production systems:

- **At-least-once** event delivery (assume duplicates)
- **Idempotent handlers** (safe retries)
- **Optimistic concurrency** and versioned event streams
- **Separation of concerns** (ports/adapters, clear boundaries)
- **Operational read model** (projection) derived from events rather than storing domain objects directly

---

## Architecture at a glance

```text
  Timer (Discover)        Event Grid Topic              Cosmos DB
       |                     |                            |
       v                     v                            v
  Table Storage --> [RequestDiscovered] --> append event -----> (events)
       |                     |
       |                     +--> Function: PrepareSubmission
       |                              |
       |                              +--> append + publish [SubmissionPrepared]
       |
       +<-- Function: CompleteRequest <-- [TerminalStatusReached] <-- Function: PollExternalJob
                                      \                                   |
                                       \                                  +--> (retry) publish [SubmissionPrepared]
                                        \                                 +--> (terminal) publish [TerminalStatusReached]
                                         \
  Timer (Poll Scheduler) -------------------> query projection (requests) -> publish [JobPollRequested]
```

### Event sourcing model

- **Event store**: Cosmos container `events`
  - Partition key: `/aggregateId` (where `aggregateId == requestId`)
  - Stream metadata doc (`id = "__stream"`) stores the current version
  - Events are appended via transactional batch (stream version + event docs)

- **Projection**: Cosmos container `requests`
  - A small, operational read model derived from events
  - Used for efficient queries like “which jobs are due to be polled?”
  - Rebuildable from the event stream

- **Idempotency**: Cosmos container `idempotency`
  - Handler-level de-duplication using a lease-based record

---

## Event catalog (CloudEvents 1.0)

All events are published to an **Event Grid Custom Topic**.

| Type | Purpose | Produced by |
|---|---|---|
| `workflow.request.discovered.v1` | A table row was claimed and admitted into the workflow | Discover timer |
| `workflow.submission.prepared.v1` | Work item is ready to be submitted (attempt N) | PrepareSubmission handler + Poll retry path |
| `workflow.job.submitted.v1` | External job was created; `externalJobId` known | SubmitJob handler |
| `workflow.job.pollrequested.v1` | A status check should be performed now | Poll scheduler timer |
| `workflow.job.terminal.v1` | External workflow reached a terminal state (`Pass`/`Fail`) | PollExternalJob handler |
| `workflow.request.completed.v1` | Intake table was updated to terminal status | CompleteRequest handler |

**External service statuses** (wire values): `Created`, `Inprogress`, `Pass`, `Fail`, `FailCanRetry`.

---

## Repo layout

| Path | Contents |
|---|---|
| `src/Workflow.Domain` | Domain model, value objects, domain events. Aggregates are rehydrated from event streams. |
| `src/Workflow.Application` | Use cases (handlers), ports (interfaces), policies, correlation. |
| `src/Workflow.Infrastructure` | Adapters: Table Storage, Cosmos (event store + projection + idempotency), Event Grid publisher, auth. |
| `src/Workflow.Functions` | Azure Functions (isolated worker). Triggers are thin and delegate to application handlers. |
| `src/ExternalService.Mock` | Sample external service with deterministic status progression; supports AAD auth or local bypass. |
| `infra/` | Bicep templates to deploy resources. |
| `docs/` | Local dev + Azure deployment guides + architecture notes. |
| `tests/` | Unit tests (projection reducer, deterministic IDs, domain primitives). |

---

## Quickstart

### Option A (recommended): Deploy to Azure

1) Provision infrastructure:

```bash
az group create --name <rg> --location <location>

az deployment group create \
  --resource-group <rg> \
  --template-file infra/main.bicep \
  --parameters infra/dev.bicepparam
```

2) Deploy the Functions app:

```bash
dotnet publish src/Workflow.Functions/Workflow.Functions.csproj -c Release -o out
cd out
zip -r ../app.zip .

az functionapp deployment source config-zip \
  --resource-group <rg> \
  --name <functionAppNameFromOutputs> \
  --src ../app.zip
```

3) Seed the intake table with a few `Unprocessed` entities.

See `docs/DEPLOY_AZURE.md` for details.

### Option B: Local development

Local end-to-end runs are possible, but **Event Grid triggers require a real Event Grid topic/subscription**. The simplest workflow is:

1) Deploy Azure resources (Option A)
2) Run the Functions app locally (Core Tools) pointed at Azure resources

See `docs/LOCAL_DEV.md`.

---

## Configuration

For local development, copy:

- `src/Workflow.Functions/local.settings.json.example` → `src/Workflow.Functions/local.settings.json`

Key settings (high level):

- `EventGrid:TopicEndpoint` / `EventGrid:TopicKey` (TopicKey optional when using Entra ID role assignment)
- `Cosmos:*` (connection and container names)
- `Table:*` (account URI for MI or connection string for local)
- `ExternalService:*` (base URL, audience, and whether auth is required)

---

## Security & identity notes

- The workflow uses `DefaultAzureCredential`.
  - In Azure: the Function App’s **system-assigned managed identity** is used.
  - Locally: `az login` credentials are typically used.

- The external service mock supports **AAD token validation** when enabled.
  - For a public repo, we keep a **local bypass mode** so contributors can run without Entra setup.

---

## Design notes (what to look for)

- **Idempotency**: Each handler uses a Cosmos idempotency record keyed by the incoming CloudEvent id.
- **Versioned streams**: Event store enforces optimistic concurrency with `expectedVersion`.
- **Projections**: Poll scheduling queries are against a projection derived from events.
- **Vertical slices**: Each function trigger delegates to an application handler.

For a deeper explanation of the event model and consistency choices, see `docs/ARCHITECTURE.md`.

---

## Operational notes (failure modes & recovery)

This reference implementation is designed for **at‑least‑once** delivery and safe retries. A concise
failure‑mode and recovery guide is in `docs/OPERATIONS.md`.

---

## Known gaps / next steps

These are intentionally omitted to keep the reference focused and approachable:

- Projection rebuild tooling / migrations / backfill safety
- Multi‑tenant partitioning & isolation concerns
- Advanced resiliency patterns (circuit breakers, bulkheads, etc.)
- High‑volume Event Grid subscription filtering strategies
- Distributed tracing beyond correlation IDs

---

## License

MIT. See `LICENSE`.
