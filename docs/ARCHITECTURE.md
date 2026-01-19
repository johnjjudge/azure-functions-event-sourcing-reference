# Architecture notes

This document explains the key architectural choices in this repository. It’s written for engineers who want to understand **why** the code is structured the way it is, and what tradeoffs are being demonstrated.

## Core goals

1. **DDD-ish structure that stays practical**
   - The Domain project expresses invariants and core concepts.
   - The Application project contains use-cases (handlers) and ports.
   - The Infrastructure project contains adapters (Azure services, HTTP clients, publishing).
   - Azure Functions are thin entrypoints.

2. **Event sourcing as the source of truth**
   - The canonical history of each workflow lives in an append-only event stream.
   - Aggregates are reconstructed by replaying events when the domain rules matter.

3. **Event-driven integration**
   - Handlers publish CloudEvents to an Event Grid custom topic.
   - Handlers assume **at-least-once** delivery and must tolerate duplicates.

4. **Operational read model for queries**
   - Event streams are great for “what happened?” but not always efficient for “what should I do now?”
   - A small projection is derived from events to support queries like “what needs polling now?”

## Bounded context

This repo intentionally uses a single bounded context (“Workflow”). In production you might split intake, execution, billing, notifications, etc. For a reference implementation, a single context keeps the code approachable.

## Event store (Cosmos)

### Partitioning

- Container: `events`
- Partition key: `/aggregateId`
- Aggregate id is the **RequestId** (`PartitionKey|RowKey`).

This yields:

- Fast “load stream by aggregate id”
- Natural horizontal scale as request volume grows

### Stream versioning

Each stream contains a metadata document:

- `id = "__stream"`
- holds the current `version`

Appends use a **transactional batch**:

1. replace the stream document with `version + N`
2. create N new event documents

If the caller supplies an `expectedVersion` that doesn’t match, the event store throws a concurrency error.

### Deterministic event ids

Handlers use a deterministic event id factory (SHA-256 → Base64Url) with a discriminator string.

This helps in two places:

1. **Crash-safe republish**: if appending succeeds but publishing fails, a retry can find the previously appended event and republish without duplicating domain history.
2. **Human debugging**: the discriminator includes attempt numbers or poll time anchors.

## Projection (Cosmos)

### Why a projection exists

Polling requires a query: “which requests are due for polling now?”

If you only have event streams, you’d either:

- scan many partitions (expensive), or
- maintain an index anyway

So this repo demonstrates a lightweight projection:

- Container: `requests`
- Partition key: `/requestId`
- Fields include: `status`, `attemptCount`, `externalJobId`, `nextPollAtUtc`, and `lastAppliedEventVersion`

### Projection is rebuildable

The projection is derived from events. In production, you’d also include:

- projection versioning
- rebuild tooling
- backfill safety

For a reference repo, we keep it intentionally small.

## Idempotency and at-least-once delivery

Event Grid is an at-least-once delivery system. Your handlers must assume:

- duplicate events
- retries after partial failures
- out-of-order delivery (rare, but possible in distributed systems)

This repo uses a Cosmos-based idempotency store:

- Container: `idempotency`
- Partition key: `/handler`
- Document keyed by `(handler, eventId)`
- Lease model prevents duplicate concurrent processing

Handlers follow a pattern:

1. `TryBeginAsync(handler, eventId)`
2. do work (append event, update projection, publish)
3. `MarkCompletedAsync(...)`

If a handler crashes after appending but before publishing, a retry can:

- load the stream
- find the relevant event (deterministic id)
- republish the CloudEvent

## Timer-based scheduling (polling)

Event Grid is not a delayed scheduler. To “run something every 5 minutes”, the repo uses:

- a **TimerTrigger** that queries the projection for `NextPollAtUtc <= now`
- publishes `workflow.job.pollrequested.v1` events

This pattern is simple, easy to observe, and aligns with many production implementations.

## Table Storage intake and leasing

The intake table provides the initial list of work items. To avoid double-processing:

- rows start with `Status=Unprocessed`
- the discoverer claims rows via ETag update:
  - set `Status=InProgress`
  - set `LeaseUntilUtc = now + leaseDuration`

If a request stalls, the lease expiry allows it to be reclaimed.

## Managed Identity to call external services

The Functions app uses `DefaultAzureCredential` and requests a token for:

- `ExternalService:Audience` + `/.default`

In Azure, the Function App’s system-assigned managed identity can be granted access to a real downstream API.

For the included mock service:

- AAD validation can be enabled (`Auth:Enabled=true`) to demonstrate the flow.
- A local bypass is included for contributor friendliness.

## What’s intentionally omitted

To keep this repo focused, we do **not** include:

- full projection rebuild tools / migrations
- multi-tenant concerns
- advanced resiliency (circuit breakers, bulkheads)
- high-volume Event Grid subscription filtering strategies
- distributed tracing configuration beyond correlation ids

Those are all valid next steps if you want to expand the repo.
