# Operations & Failure Modes

This note summarizes how the workflow behaves under retries, partial failures, and projection lag, plus the
expected recovery actions. It complements `docs/ARCHITECTURE.md`.

## Retry and failure behavior

### Discovery (timer trigger)
Failure modes:
- **Table row claimed, event append fails** → row remains `InProgress` with a lease; it will be reclaimed once the lease expires.
- **Event append succeeds, publish fails** → a later discovery pass sees the stream and re‑publishes the stored event (deterministic IDs).

Recovery:
- Let the lease expire and the timer reclaim the row, or manually reset the table row to `Unprocessed`.

### PrepareSubmission (Event Grid)
Failure modes:
- **Idempotency lease conflict** → handler exits; retry later.
- **Append succeeds, publish fails** → retry re‑publishes the stored event.

Recovery:
- No manual action required. Event Grid retries plus deterministic IDs handle the replay.

### SubmitJob (Event Grid)
Failure modes:
- **External call succeeds, append fails** → no domain event; retry re‑submits (external service mock is idempotent per RequestId+Attempt).
- **Append succeeds, publish fails** → retry re‑publishes the stored event.

Recovery:
- No manual action required; retries are safe by design.

### PollExternalJob (Event Grid)
Failure modes:
- **Poll returns non‑terminal** → no event appended; next poll time already advanced by scheduler.
- **Terminal/retry event appended, publish fails** → retry re‑publishes the stored event.

Recovery:
- No manual action required; retries safe by design.

### CompleteRequest (Event Grid)
Failure modes:
- **Table update succeeds, append fails** → request marked terminal; retry can still append completion.
- **Append succeeds, publish fails** → retry re‑publishes stored completion event.

Recovery:
- No manual action required; retries safe by design.

## Projection lag and rebuild

The projection is rebuildable from the event stream. If it becomes stale or inconsistent, the supported
recovery approach is to re‑run the reducer and upsert the projection.

For a reference implementation, there is intentionally no bulk rebuild tooling; it is a deliberate omission
noted in `docs/ARCHITECTURE.md`.

## Manual recovery shortcuts (for demos)

If you need to “unstick” a demo quickly:
- Set the intake row back to `Unprocessed` and clear `LeaseUntilUtc`.
- Delete or reset the projection document; it will be rebuilt on the next handler pass.
