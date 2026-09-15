# ADR-008: PostgreSQL durable reasoning jobs with fenced safe replay

Status: Accepted for Issue #27 (T7)

## Decision

Reuse PostgreSQL and the existing maintenance orchestrator. Application owns job
contracts and attempt processing; Infrastructure owns queue SQL and lease fencing;
API authenticates submissions/observations; Worker runs two independent hosted
services for reasoning and existing dispatch reconciliation. No broker, generic
scheduler, new provider or parallel workflow engine is introduced.

ReasoningJob owns scheduling: Queued, Running, Succeeded, Failed, Cancelled.
MaintenanceRun remains the business aggregate. Queued submission creates a Queued
run atomically. Successful job processing means the run reached WaitingForApproval,
not business completion. Blocked reasoning is a Failed job with Blocked phase and
the existing Blocked business state. No lease is retained for human review.

## Submission and ownership

The authenticated actor plus submission key is unique. A transaction advisory lock
serializes concurrent identical keys; a canonical typed request hash distinguishes
equivalent retries from 409 conflicts. Generated IDs/correlation are excluded;
normalized symptom, sorted trusted candidates, evidence and narrative culture are
included. Keys are bounded ASCII identifiers. Credentials/permissions are not job
payloads. Actor permissions are re-resolved by the trusted Worker host and narrowed
to read/start for the submitted equipment; claims never grant approval or dispatch.

Bounded discovery uses `FOR UPDATE SKIP LOCKED` over eligible queued or expired
Running rows, scoped to configured Worker equipment. A claim has a random fencing
token, owner, expiry and attempt execution ID. PostgreSQL time governs leases.
Heartbeat renews every quarter lease. Business/trace writes first lock and validate
the current job/token/expiry and absence of cancellation in their own transaction.
Cancellation, takeover and writes therefore serialize at one job row, before run
and order locks. No connection/transaction spans provider execution. A lease valid
when that row lock is acquired is the write's linearization point; takeover waits
for the short transaction to finish. No stale owner can publish after takeover.

## Recovery and retry boundary

Recovery is **safe whole-pipeline replay before atomic review publication**, not
exact mid-agent continuation. Stable run/order IDs survive attempts. Each new
attempt gets a separate execution/trace linked by job and correlation. If an order
already committed with its proposal and WaitingForApproval run, recovery completes
the job without rerunning agents. Thus a crash between publication and job
acknowledgement cannot create a second order. No reasoning attempt executes tools
with external dispatch authority.

Three attempts maximum. Graceful interrupted attempts requeue with 2^attempt seconds
backoff; crashed attempts become eligible after expiry. Exhaustion fails the run.
Observed invalid output, insufficient evidence, deterministic safety failures,
authorization failures and permanent pipeline failures are terminal, not blindly
retried. No additional provider retry loop is introduced. Lost infrastructure before
a terminal state can be persisted is treated as an interrupted ownership attempt.

## Cancellation, observation and accounting

Queued cancellation acknowledges without execution. Running cancellation atomically
records job/run intent; heartbeat signals the orchestrator token. Acknowledgement
or recovery completes Cancelled. Shutdown/lost lease is not user cancellation.
If review publication won the transaction race, cancelling processing conflicts
and cannot undo business review, approval or external effects.

Progress is a persisted safe event log plus latest phase/version, with no fake
percentage or hidden reasoning. GET SSE replays bounded pages and polls; disconnect
only ends observation. `Last-Event-ID` resumes after a known event. Existing
request-owned run endpoints and Angular behavior remain compatible and explicitly
separate. The durable endpoint and deterministic harness demonstrate T7 without a
new UI dashboard.

Culture is persisted explicitly and affects advisory narrative only. Original
citations and authoritative executable/safety definitions are unchanged. Attempt
trace hierarchy and reported usage remain intact. An interrupted trace may be
incomplete; no token or monetary usage is fabricated for an unobserved provider
response. Job attempt records expose interruption independently of trace completion.

## Limitations and alternatives

An in-memory queue cannot survive restart and is rejected. A broker adds deployment
and distributed transaction complexity unnecessary for current throughput. Exact
agent checkpoint serialization and a generic state machine are premature. Polling
adds up to one configured poll interval; cancellation detection up to a quarter
lease. Non-cooperative provider computation may outlive its lease and consume
resources, but its writes are fenced. Partial computational work and charges can
repeat on replay; external dispatch cannot. Retention/pruning and multi-step job UI
are separate future work. Keep the existing dispatch reservation/receiver
idempotency and reconciliation path authoritative.

Migration 005 is additive and reproducible. Real PostgreSQL concurrency/fencing
tests and a separate-process kill/restart demo validate the durability boundary.
