# API, streaming and reconciliation Worker

The Angular workspace uses these existing contracts. See [frontend setup and end-to-end demo](FRONTEND-SETUP.md) for the development proxy, HTTPS deployment, session credentials and browser workflows.

## Prerequisites and configuration

Use .NET SDK 10 and PostgreSQL with pgvector (`pgvector/pgvector:pg17` is used by CI).
The deployment migration identity needs permission to create the vector extension,
schemas and tables. Runtime identities should use only required data privileges.
There is no live ERP requirement; `PostgresInbox` records durable dispatch tickets
in a separately committed receiver schema/pool.

Copy `docs/examples/host-settings.json` to a private configuration file outside
source control. Set `MAINTENANCE_CONFIG` to its absolute path. Replace model and
embedding dimension/revision placeholders. Environment variables override this
file. `appsettings` remains available for ordinary host logging configuration.

Copy/review `docs/examples/reviewed-procedures.json` for the actual equipment and
manual revision, and set `Safety__ProcedureFile` to its absolute path. This file
is an illustration, not a complete industrial safety procedure. Competent site
operators must review the full instruction/requirement coverage. Never generate
authoritative procedure configuration from model output. Unknown instructions,
description or manual revision fail closed. No mandatory requirements requires
explicit `ExplicitlyNoMandatoryRequirements: true` in trusted configuration.

Required environment values (placeholders only):

```powershell
$env:MAINTENANCE_CONFIG = '<absolute path to private host-settings.json>'
$env:Safety__ProcedureFile = '<absolute path to reviewed procedures.json>'
$env:ConnectionStrings__Operations = 'Host=<host>;Database=<operations>;Username=<user>;Password=<secret>'
$env:ConnectionStrings__Knowledge = 'Host=<host>;Database=<knowledge>;Username=<user>;Password=<secret>'
$env:ConnectionStrings__DispatchReceiver = 'Host=<host>;Database=<receiver>;Username=<user>;Password=<secret>'
$env:Worker__EquipmentIds = '<equipment-guid>,<another-equipment-guid>'
```

Operations/knowledge/receiver may use one development database; the receiver
always uses a distinct connection pool. The Worker needs Operations, receiver,
procedure configuration and its equipment allowlist. Reasoning is enabled by
default and additionally needs Knowledge, LLM/embedding settings and the same
server-owned actor/resource mappings as the API. Set `Worker:ReasoningEnabled` to
`false` for a reconciliation-only deployment.

For API authentication, supply one or more server-owned credential mappings:

```powershell
$env:Authentication__Credentials__0__Actor = '<operator-identity>'
$env:Authentication__Credentials__0__Secret = '<unique random secret of at least 32 characters>'
$env:Authentication__Credentials__0__Permissions = 'read,start,approve,verify,dispatch'
$env:Authentication__Credentials__0__EquipmentIds = '<equipment-guid>'
```

For separate supervisors/technicians/dispatchers, configure additional numbered
entries with only their required permissions. No client `actorId` or role fields
are accepted. Tokens are opaque bearer secrets, not self-signed claims or JWTs;
there is no token issuance endpoint. Missing/invalid configuration fails startup.
Use a secret manager/environment in deployment, never commit tokens or passwords.

Development HTTP is loopback-only. Production API requests require HTTPS at
Kestrel; forwarded headers are not trusted by default. Configure certificates via
standard ASP.NET Core hosting settings. Development OpenAPI is `/openapi/v1.json`.
`/health/live` reports process liveness; `/health/ready` checks required database
schemas without contacting live LLM providers.

## OpenAI or Ollama

The example selects Ollama with placeholder model names. Install a tool-capable
chat model and an embedding model, configure their actual names and dimensions,
and pin `Knowledge:EmbeddingRevision` to the deployed weights/preprocessing.

For hosted selection, set `Llm:PrimaryProvider` and `Llm:EmbeddingProvider` to
`OpenAi`, provide the `OpenAi` endpoint/model/timeouts block and supply
`Llm__OpenAi__ApiKey` from secrets. See the full
[provider settings](../src/IndustrialCopilot.Infrastructure/AI/README.md) and
[knowledge pipeline settings](../src/IndustrialCopilot.Infrastructure/Knowledge/README.md).
Embeddings never silently fall back. Changing embedding space requires a new
profile and reingestion. Startup validates configuration without probing provider
availability; actual ingestion/reasoning requires the chosen provider to be running.

## Deployment and startup

From the repository root, with the above configuration in the process environment:

```powershell
dotnet build
dotnet run --project src/IndustrialCopilot.Api -- --migrate
dotnet run --project src/IndustrialCopilot.Api -- --ingest '<manual-guid>' '<revision-guid>' '<absolute UTF-8 text file path>'
dotnet run --project src/IndustrialCopilot.Api -- --urls http://localhost:5080 --environment Development
```

The explicit migration command applies operational migrations 001-005, knowledge
schema and receiver schema, and registers configured equipment/manual identities.
Normal startup never silently migrates. Ingestion is an operator-only CLI using
the existing `ManualIngestionService`; only configured revisions are accepted.
It calls the selected embedding provider. Input is UTF-8 plain text, not raw PDF/OCR.
An empty source does not erase prior knowledge.

In a separate terminal with Worker configuration:

```powershell
dotnet run --project src/IndustrialCopilot.Worker
```

Worker defaults/ranges:

| Setting | Default | Range |
|---|---:|---:|
| `Worker:IntervalSeconds` | 30 | 5–3600 |
| `Worker:BatchSize` | 20 | 1–100 |
| `Worker:Concurrency` | 4 | 1–16 |
| `Worker:AttemptTimeoutSeconds` | 30 | 1–300 |
| `Streaming:Capacity` | 32 | 4–128 |
| `Streaming:WriteTimeoutSeconds` | 10 | 1–60 |

Each configured worker must be authorized for equipment whose dispatch attempts it
is expected to reconcile. Discovery is a trusted deployment capability, not an HTTP
endpoint. Only reconciliation runs automatically; definitive rejection does not
trigger an automatic resend.

## REST surface

All `/api` routes require `Authorization: Bearer <configured secret>`.
Optional `X-Correlation-ID` must be a nonempty UUID. The response echoes/generated
correlation ID; it never grants access.

| Method/path | Purpose |
|---|---|
| `POST /api/runs` | Run reasoning to its human-review or terminal boundary |
| `POST /api/runs/stream` | Same workflow with SSE observations |
| `GET /api/runs/{id}` | Durable lifecycle and bounded work-order/execution links |
| `GET /api/work-orders/{id}` | Current scope, target token and trusted safety state |
| `GET /api/work-orders/{id}/evidence` | Labelled original-proposal citations |
| `POST /api/work-orders/{id}/submit` | Submit a draft where its lifecycle permits |
| `POST /api/work-orders/{id}/edited-safety-preview` | Server assessment of edited content |
| `POST /api/work-orders/{id}/decisions` | Approve, Reject, or EditAndApprove |
| `POST /api/work-orders/{id}/verifications` | Record an authorized physical observation |
| `POST /api/work-orders/{id}/dispatch` | Reserve/check/deliver through Application gate |
| `GET /api/dispatch-attempts/{id}` | Current durable attempt outcome/reference |
| `GET /api/traces/{id}` | Safe execution/status inspection |

Requests use explicit DTOs; unknown fields, malformed IDs/text and oversized bodies
are rejected. Reload the review after each mutation: the concurrency token changes
even when Domain Revision does not. Stale tokens/lifecycle conflicts return 409;
safety mismatch returns 422. Unknown external acceptance returns 202, never a false
successful dispatch. Safe dependency failures return 503 and timeouts 504.

## Workflow and SSE demo

Example PowerShell requests, using only a token already supplied through environment:

```powershell
$base = 'http://localhost:5080'
$headers = @{ Authorization = "Bearer $env:Authentication__Credentials__0__Secret" }
$body = @{ equipmentId = '<configured-equipment-guid>'; symptom = 'vibration' } | ConvertTo-Json
$run = Invoke-RestMethod "$base/api/runs" -Method Post -Headers $headers -ContentType application/json -Body $body
$review = Invoke-RestMethod "$base/api/work-orders/$($run.workOrderId)" -Headers $headers
```

For streaming, use a separate new workflow request:

```powershell
curl.exe -N -H "Authorization: Bearer $env:Authentication__Credentials__0__Secret" -H 'Content-Type: application/json' --data $body "$base/api/runs/stream"
```

Events include WorkflowStarted, AgentStarted/AgentCompleted, SafetyEvaluated,
WorkOrderReady, WaitingForApproval, Blocked, Cancelled or Failed, followed by Result
when available. They contain identifiers/status, not model reasoning, prompts or
full manual contents. WaitingForApproval ends this execution stream, not the durable
run. Slow clients hitting the buffer/write bound cause cancellation. Disconnects
cancel active reasoning; already published human-review scope remains durable.

Choose one supervisor decision on a pending revision. Plain Approve/Reject uses
`{ target: <review.target>, decision: "Approve" | "Reject" }`.
For EditAndApprove, choose content covered by trusted procedure configuration:

```powershell
$edited = $review.content # Apply the intended reviewed edits here.
$preview = Invoke-RestMethod "$base/api/work-orders/$($run.workOrderId)/edited-safety-preview" -Method Post -Headers $headers -ContentType application/json -Body ($edited | ConvertTo-Json -Depth 10)
$decision = @{ target=$review.target; decision='EditAndApprove'; editedContent=$edited; reviewedRequirements=$preview.requirements }
Invoke-RestMethod "$base/api/work-orders/$($run.workOrderId)/decisions" -Method Post -Headers $headers -ContentType application/json -Body ($decision | ConvertTo-Json -Depth 10)
```

The server reassesses and compares the echoed requirements. Omitting a mandatory
requirement or changing its ID/description/flag is rejected. Arbitrary scope
expansion beyond configured procedures blocks. EditAndApprove creates and approves
the final revision once; all old physical verifications are cleared.

After an authorized technician has actually performed a prerequisite check, reload
the review and POST a verification containing `target`, `prerequisiteId`, `evidence`
and `satisfied`. Never automatically mark the checklist satisfied from model output.
Reload after every verification. An authorized dispatcher then POSTs the current
`review.target` to `/dispatch`. The API does not accept approval/safety booleans on
that endpoint. Poll the returned attempt ID. Confirmed means accepted by the demo
inbox, not physically executed maintenance.

## Recovery and tests

Worker discovery reads durable Pending/Uncertain rows, advances next eligibility,
and reconciles the same UUID. If the API disappears after inbox acceptance but
before internal confirmation, a later Worker instance can confirm the retained
attempt. If acceptance remains unknown, the scope stays frozen. No new delivery
key is generated and no in-memory queue is required.

The deterministic restart/rollback tests exercise this window without a live ERP:

```powershell
dotnet test tests/IndustrialCopilot.Api.Tests/IndustrialCopilot.Api.Tests.csproj
dotnet test tests/IndustrialCopilot.Worker.Tests/IndustrialCopilot.Worker.Tests.csproj
dotnet test tests/IndustrialCopilot.IntegrationTests/IndustrialCopilot.IntegrationTests.csproj --filter 'FullyQualifiedName~ReconciliationDiscoveryTests'
dotnet test
git diff --check
```

For local database tests, set `RAG_TEST_POSTGRES` to an **isolated** pgvector server
with permission to create/drop test databases. Without it, those tests skip.
CI supplies this server and executes the full suite. Tests use scripted LLMs and
fake/durable demonstration receivers. No live OpenAI/Ollama/ERP validation is claimed.
The existing Microsoft.OpenApi NU1903 advisory remains a separate dependency issue.

## Durable T7 reasoning jobs

Apply migration 005 before starting either host. PostgreSQL is the sole queue;
API submission does not contact a model. Send `Idempotency-Key` (1-128 ASCII
letters/digits or `-_.:`), `Accept-Language` and authenticated credentials:

- `POST /api/jobs` with `{ "equipmentId": "<uuid>", "symptom": "pump vibration" }`
  returns 202, stable job/run identifiers, status/progress URLs.
- `GET /api/jobs/{jobId}` returns safe scheduling status, phase, version,
  cancellation intent, attempt/execution links and eventual result.
- `GET /api/jobs/{jobId}/events` observes persisted execution events and a final
  result. `Last-Event-ID` resumes after an observed numeric sequence. Disconnect
  stops observation only. Polling is bounded, one second, with bounded write timeout.
- `POST /api/jobs/{jobId}/cancel` requires `start` and `read` on its equipment.
  Queued cancellation acknowledges immediately; active cancellation is durable
  intent until acknowledged. Already-published/terminal processing returns 409.

Submission requires `start` and `read`; inspection/SSE requires `read`. Worker
execution revalidates the submitting actor against host configuration and narrows
its context to read/start for that one equipment. No bearer secret or client role
is persisted in the job. Use the same trusted mappings on API and Worker. Changes
to file configuration require restarting the affected hosts, as before.

The same actor/key and normalized symptom/candidates/culture converge, including
concurrent retries; changed input is `409 submission_conflict`. IDs generated by
the API and correlation IDs do not define idempotency. Cancellation conflict uses
`cancellation_conflict`; safe human-facing errors follow request localization.

| Reasoning setting | Default | Range |
|---|---:|---:|
| `Worker:ReasoningConcurrency` | 2 | 1-4 |
| `Worker:LeaseSeconds` | 30 | 4-300 |
| `Worker:PollSeconds` | 1 | 1-60 |

Reasoning and reconciliation run as independent hosted services. Renewal is every
quarter lease. Three attempts maximum; graceful release uses 2^attempt seconds
backoff, crashes wait for lease expiry. Only interrupted ownership replays; model,
safety, authorization and dependency failures observed by the pipeline terminate.
A provider that ignores cancellation may still consume compute; the database lease
fence prevents its old attempt from publishing or changing current business state.
No new owner claims a still-valid lease. No transaction spans a model invocation.

Job Succeeded means review publication, while MaintenanceRun is WaitingForApproval.
Separate human decisions, trusted verification and dispatch remain mandatory.
Attempts retain independent execution IDs/traces under one correlation/job/run;
a crashed attempt's trace may be incomplete and its usage must not be invented.

### Text/PDF ingestion and status

The existing `--ingest` command now accepts `.txt` and `.pdf`. Supply optional `--ingest-metadata <title> <source-label> <positive-revision-number>` for new sources. Inspect `--ingestion-status <manual-guid> <revision-guid>` for durable attempt states and safe failure categories. The configured manual/revision allowlist still applies. Run knowledge migrations before ingestion. Full limits, synthetic corpus commands and interruption semantics: [CORPUS-INGESTION](CORPUS-INGESTION.md).

## Product Ask and ingestion (Issue #35)

Apply the existing `--migrate` command before deployment; it now includes operational migration 006 (conversations/ask_turns). Keep all existing knowledge migrations. Optional `Ask:TimeoutSeconds` defaults to 60 and accepts 1–60. Give document operators the explicit `ingest` permission in addition to their equipment scope; this is not implied by `approve`. Existing API authentication, actor-wide POST limits, safe errors and CORS apply unchanged.

| Method/path | Contract |
|---|---|
| GET `/api/identity` | Trusted actor, role label, permissions, equipment IDs; no credentials |
| POST `/api/conversations` | EquipmentId, DocumentId, ManualRevisionId; culture from Accept-Language |
| GET `/api/conversations?offset=0&limit=20` | Current actor's authorized history; limit 1–50, offset 0–10000 |
| GET `/api/conversations/{id}?after=0&limit=20` | Owned conversation plus turns after sequence; bounded 1–50 |
| POST `/api/conversations/{id}/ask` | JSON Question, optional TopK (default5, 1–10); SSE |
| POST `/api/documents/{document}/revisions/{revision}/ingest` | Raw text/plain or application/pdf body; query equipmentId, filename, title, revisionNumber |
| GET `/api/documents/{document}/revisions/{revision}/ingestion?equipmentId=...` | Bounded per-attempt status, page/chunk counts and safe failure category |

Use a provisioned equipment ID and stable nonempty document/revision GUIDs. New source associations do **not** make procedures executable or approved. Reverse proxies must allow the upload route's 16,000,000-byte bound and disable response buffering for Ask SSE. Other API bodies retain the existing 128 KiB limit. Bearer headers, not query tokens, authenticate streams. Forward Accept-Language, X-Correlation-ID and disconnect cancellation.

Ask events: meta/evidence/delta/citation/completed/error. No event IDs or resume promise: a retry is a new explicit request. Read persisted history after uncertain completion. History states are 0 Streaming, 1 Completed, 2 InsufficientEvidence, 3 Cancelled, 4 Failed. Ask disconnect cancels generation; T7 observation disconnect never cancels its durable job. See [ADR-012](adr/ADR-012-request-owned-ask-and-conversations.md).
