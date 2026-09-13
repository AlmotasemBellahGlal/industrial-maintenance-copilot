# ADR-006: Trusted API hosts, edited-scope safety and durable reconciliation polling

- Status: Accepted
- Scope: Issue #21; additive prerequisites explicitly approved by the human reviewer.

## Exact reviewed scope

The agent-oriented ISafetyPolicy needs a DiagnosticPlan. Only the original
WorkOrderProposal is persisted, so inventing a plan for HTTP edits would fabricate
provenance. IExecutableSafetyPolicy instead accepts exact WorkOrderContent and
returns authoritative SafetyAssessment. ExactProcedureSafetyPolicy implements both
ports against the same reviewed procedure configuration; endpoints contain no
procedure matching or industrial safety inference.

The supervisor previews edited content with `edited-safety-preview`, then submits
the content and the exact returned requirement definitions. These definitions are
comparison-only. ExecutableReviewService checks authorization/current target,
reassesses the edited scope, rejects mismatches, and passes server-owned requirements
to the established EditAndApprove operation. HostAccess independently validates the
edited scope at the existing approval policy boundary. Persistence still performs
the final revision/token check under lock and atomically creates/approves the new
revision, clearing verification. No second approval is required. A configuration
change between preview and decision fails closed if the requirement set changes.

## Host identity and HTTP

API authentication uses ASP.NET Core AuthenticationHandler with explicitly
configured opaque bearer credentials. Each credential maps to one server-owned
actor, a permission set and an equipment allowlist. Comparison uses fixed-time
hash comparison. Secrets come from environment/secret configuration, never request
bodies or source control. Unknown actor fields in JSON are rejected. Authorization
is independent for read/start, supervisor approval, verification and dispatch.
The Worker has an explicit deployment-owned service identity with only dispatch
permission and configured equipment access.

No default credentials or permissive authorization exist. HTTPS is mandatory in
production; development/test HTTP is restricted to loopback clients. Forwarded
headers are not blindly trusted: deployments must terminate TLS at Kestrel or add
their own explicitly trusted proxy integration. This is a small demo identity
mechanism, not a complete identity platform or token issuance service.

HTTP binds explicit DTOs, limits bodies to 128KiB, validates IDs/text/orders/targets,
and maps safe outcomes to 400/401/403/404/409/422/503/504. Dispatch requests use
DispatchCoordinator; controllers never invoke Domain.Dispatch or external adapters.
Unknown delivery returns 202, definite nonacceptance 422; inspecting an existing
attempt returns 200 regardless of its business state. Original proposal citations
are labelled historical and never presented as proof of subsequently edited scope.
Trace endpoints project safe operation names/status/error codes, not prompts.

## Streaming and cancellation

POST `/api/runs/stream` is request-owned SSE. Typed Application progress observations
are emitted at actual pipeline boundaries, independently of trace persistence.
There is no chain-of-thought, model text or full manual content in events. Incoming
UUID correlation is validated or generated and propagated to responses and traces.

Buffer capacity defaults to 32 (range 4–128). A full buffer cancels execution rather
than dropping arbitrary progress or blocking the producer indefinitely. Each socket
write has a configurable deadline, default 10 seconds. RequestAborted propagates
to reasoning/provider calls. The producer is awaited during shutdown. Domain
cancellation/failure finalization retains its separate cleanup budget. Durable
publication at the human-review boundary is not undone by a disconnected client.

WaitingForApproval ends this execution stream; it does not mean the run is Completed.
No generic queued orchestration or automatic restart of interrupted reasoning is
introduced. The original queue-oriented architecture remains a future target;
current asynchronous durable background execution is dispatch reconciliation.

## Worker discovery and restart

IReconciliationDiscovery returns at most 100 attempt IDs. PostgreSQL selects Pending
and Uncertain attempts by `next_reconciliation_at, attempt_id`, using a short
transaction with SKIP LOCKED to advance eligibility by a bounded defer period.
This prevents immediate hot-loop rediscovery and gives older eligible attempts
priority. It is not a delivery lease or authority to mutate the reserved scope.
A crash before processing merely delays rediscovery until eligibility returns.

ReconciliationBatch calls DispatchCoordinator.ReconcileAsync with the existing ID,
bounded concurrency and per-attempt cancellation deadlines. One failure does not
end the batch. Existing per-attempt coordination serializes concurrent workers;
confirmation remains atomic. No worker path calls SendAsync, reserves a new key,
or automatically retries a definitively rejected delivery. Nonacceptance follows
Issue #19 semantics; explicit trusted retry is separate.

Pending/Uncertain rows survive API death. Receiver acceptance followed by a lost
internal confirmation is recovered by new discovery/coordination instances and
the same key. If the receiver cannot establish acceptance/nonacceptance, Uncertain
stays frozen and remains eligible after deferral. In particular, absence from the
demonstration receiver is not proof that a prior invocation cannot still accept.

Worker defaults: 30-second polling, batch 20, concurrency 4, 30-second attempt
deadline. BackgroundService owns scheduling/logging only; Application owns the
batch and dispatch semantics. PostgreSQL is the source of truth; no in-memory queue
is required for restart. Adapters must honor cancellation and idempotency contracts.

## Composition, deployment and verification

API registers existing LLM/RAG/operational adapters, reviewed procedures, host
authorization, trusted retrieval, orchestrator, approval and dispatch. Worker uses
the same operational/receiver configuration without requiring LLM configuration.
Receiver connections use a separate pool. Explicit migrations add deferred
reconciliation eligibility and seed configured operational manual identities.
An operator-only ingestion command invokes the existing text ingestion pipeline.

Liveness is process-only. Readiness checks operational discovery schema, receiver
schema and knowledge schema; it does not probe live LLM availability. OpenAPI is
available in Development. Tests use scripted providers, real loopback HTTP and
isolated PostgreSQL, including confirmation recovery and concurrent discovery.
