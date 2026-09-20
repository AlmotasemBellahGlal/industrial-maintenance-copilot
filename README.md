# Industrial Maintenance Copilot

An agentic RAG platform for industrial field maintenance, built for the
ITI Technical Instructor Assessment.

## Assigned Variant

- **Domain:** D5 — Industrial / Field Maintenance
- **Mandatory Twist:** T7 — Async Long-Running Jobs

> The variant was assigned by the ITI assessment organisers. The D5/T7
> combination was not self-selected. Course code and cohort details are
> withheld from this public repository per the programme privacy policy.

## Project Goal

The system helps maintenance technicians identify relevant equipment manuals,
retrieve grounded diagnostic guidance with citations, enforce safety
prerequisites, and generate draft work orders that require supervisor approval
before dispatch.

---

## Prerequisites

**Docker-only evaluator** (no host .NET, Node or PostgreSQL required):

| Requirement | Version | Notes |
|---|---|---|
| Docker Desktop | 4.x+ with Linux containers | Enable Linux container mode |
| Docker Compose | v2+ (bundled with Docker Desktop) | |
| Git | Any modern version | |
| RAM | ≥ 4 GB available to Docker | |
| Disk | ≥ 4 GB free on Docker's storage drive | |
| Network | Internet access for first build | Image/package downloads only |

**Developer build** (host setup):

| Requirement | Version | Notes |
|---|---|---|
| .NET SDK | 10.0.x | `dotnet build` and `dotnet test` |
| Node.js / npm | 26 / 11 | Angular frontend |
| PostgreSQL + pgvector | 16+ / pg17 | `RAG_TEST_POSTGRES` for integration tests |
| Python | 3.10+ | `scripts/security/scan.py` |

---

## Docker-Only Evaluator Quick Start

**15-minute path. You need only Docker Desktop and Git.**

No host .NET, Node, Angular CLI or PostgreSQL is needed. The default mode uses
a synthetic deterministic provider — no OpenAI API key required.

### Step 1 — Clone and configure

```sh
git clone https://github.com/AlmotasemBellahGlal/industrial-maintenance-copilot.git
cd industrial-maintenance-copilot
cp .env.example .env
```

On PowerShell: `Copy-Item .env.example .env`

### Step 2 — Build and start

```sh
docker compose build --builder default api web
docker compose up -d --wait
```

The startup sequence is automatic:
- `setup` generates random credentials and an internal TLS certificate.
- `db` starts PostgreSQL with pgvector (TCP-ready health check).
- `migrate` runs all 9 migrations on a fresh empty database.
- `seed` ingests the canonical synthetic pump manual.
- `api` and `worker` start and become healthy.
- `web` (production Angular + Nginx) becomes healthy.

`up -d --wait` blocks until all services are healthy or a prerequisite fails.

### Step 3 — Get credentials

```sh
docker compose run --rm --no-deps credentials
```

Output example (tokens are randomly generated per installation):
```
SYNTHETIC LOCAL DEMO ONLY
Supervisor: 7145a1d836ff8bf8f0e2351708b5ca4a...
Technician: 4cc1ebdc8ed239273dca6b483a00e9c6...
```

These tokens are unique per installation and stored in Docker named volumes.
They are never committed to source control.

### Step 4 — Ingest assessment corpus

```sh
docker compose run --rm --no-deps corpus
```

Ingests 31 synthetic maintenance documents / 150 PDF pages through the FR-1
pipeline. Takes ~30 seconds. Safe to run twice (idempotent).

### Step 5 — Open the UI

Open **http://127.0.0.1:8080** in your browser.

Select **Connection** and paste the Supervisor token.
English/Arabic and LTR/RTL are available via the language selector.

---

## Environment Variables

All variables are documented in `.env.example`. Defaults need no API key.

| Variable | Default | Description |
|---|---|---|
| `COMPOSE_PROJECT_NAME` | `maintenance` | Docker Compose project namespace |
| `WEB_PORT` | `8080` | Host port for the Angular frontend |
| `DEMO_PROVIDER` | `Demo` | LLM provider: `Demo`, `OpenAi`, or `Ollama` |
| `OPENAI_API_KEY` | *(empty)* | Required only for `DEMO_PROVIDER=OpenAi` |
| `CHAT_MODEL` | `demo-test-v1` | Chat model name (real model for OpenAI/Ollama) |
| `EMBEDDING_MODEL` | `demo-test-v1` | Embedding model name |
| `OLLAMA_ENDPOINT` | `https://host.docker.internal:11434/` | Ollama base URL (HTTPS required) |
| `EMBEDDING_PROFILE` | `synthetic-demo-v1` | Embedding space profile |
| `EMBEDDING_REVISION` | `deterministic-four-features-v1` | Profile revision |
| `EMBEDDING_DIMENSIONS` | `4` | Vector dimensions |

**Keep `.env` private and untracked.** It is in `.gitignore`.
Never commit it. Use `.env.example` as the template.

---

## No-Paid-Key Demo Path

`DEMO_PROVIDER=Demo` is the default and the only automatically proven mode.
It uses a deterministic synthetic provider with no inference cost.

The demo still exercises the full workflow: hybrid retrieval, grounded answers
with exact citations, refusal on low evidence, multi-agent diagnosis, T7 durable
jobs, Worker crash/recovery, supervisor approval, safety verification, and dispatch.

**To use a real provider:**

*OpenAI:*
```sh
# In .env:
DEMO_PROVIDER=OpenAi
OPENAI_API_KEY=sk-...
CHAT_MODEL=gpt-4o
EMBEDDING_MODEL=text-embedding-3-small
EMBEDDING_PROFILE=openai-3small-v1    # use a profile matching real vectors
EMBEDDING_DIMENSIONS=1536
```

*Local Ollama:*
- An already-running Ollama service accessible via HTTPS (plain HTTP is rejected
  by the provider's transport policy — see `docs/DEPLOYMENT.md`).
- A compatible model installed on the Ollama host.
- A trusted TLS endpoint (e.g. an existing HTTPS gateway in front of Ollama).

**Never mix embedding profiles or dimensions** between corpus ingestion and
retrieval. Use a fresh Compose project for a different provider.

---

## 5-Minute Demo Path

This numbered script shows the key assessment claims in under 5 minutes.
All commands assume the Docker stack is running and corpus is ingested.

**Pre-requisite:** You have the Supervisor token from `docker compose run --rm --no-deps credentials`.

### 1 — Document ingestion (FR-1)

```sh
docker compose run --rm --no-deps corpus corpus-status
```
Expected: 31 entries, all `"State":"Completed"`.

### 2 — Grounded answer with exact citation (FR-2)

Open http://127.0.0.1:8080. Connect as Supervisor.

Navigate to **Ask**, enter equipment ID `11111111-1111-1111-1111-111111111111`
and document ID `22222222-2222-2222-2222-222222222222`.

Ask: **pump vibration**

Expected: streaming answer appears token by token, followed by a citation showing
exact `locator` (e.g. `text:lines 1-5`) and `snippet` from the canonical document.

### 3 — Correct refusal (FR-3 adversarial)

In the same conversation, ask: **quasar astrophysics**

Expected: the system responds with an insufficient-evidence message. No citation.
No hallucinated maintenance guidance.

### 4 — Multi-agent workflow and live progress (T7 + D5)

Navigate to **Diagnosis**. Enter:
- Equipment ID: `11111111-1111-1111-1111-111111111111`
- Symptom: `pump vibration`

Click **Start**. The UI shows live progress:
`WorkflowStarted → SymptomMatcher → DiagnosticSafetyPlanner → SafetyEvaluated → WorkOrderGenerator → WaitingForApproval`

Expected: three agent stages complete and a work order appears in **Review**.

### 5 — Approval gate enforced

In **Review**, click **Dispatch** before approving.

Expected: **422 Unprocessable Entity** — dispatch is blocked. The system
requires supervisor approval first.

Now click **Approve** and record the approval.

Attempt dispatch again.

Expected: **Still blocked** — safety prerequisites must also be verified.

Navigate to **Safety Verification**, record an observation for each prerequisite.

Now dispatch succeeds.

### 6 — Trace and usage observability

From the completed run, navigate to the **Trace** view.

Expected: all three agent steps visible with status, timing, and the
`tool: retrieve_evidence` call under SymptomMatcher.

Navigate to **Usage** (top nav).

Expected: LLM usage records visible, with `tokens: null` and `estimatedCost: null`
for the deterministic provider (honest: no token count from a synthetic model).

### 7 — T7 Worker crash and recovery

```sh
# In a terminal:
docker compose stop worker
docker compose run -d --no-deps --name test-recovery-worker -e DEMO_MODEL_DELAY_MS=30000 worker

# Submit a job (via the UI or smoke tool), wait for it to be Running.
# Then kill the delayed worker:
docker rm -f test-recovery-worker
docker compose start worker
```

Expected: the Worker picks up the orphaned job with a new attempt.
The work order appears with `attempts.length == 2` in the trace.

---

## Running Tests

### .NET tests

```sh
dotnet build         # must be clean (0 warnings/errors)
dotnet test          # all deterministic tests (no external deps)
```

With PostgreSQL integration tests:
```sh
$env:RAG_TEST_POSTGRES = "Host=127.0.0.1;Port=5432;Database=postgres;Username=postgres;Password=<password>"
dotnet test
```

### Angular tests

```sh
cd src/IndustrialCopilot.Web
npm ci
npm run typecheck
npm test
npm run build
npx playwright install --with-deps chromium
npm run test:e2e
```

### Security scans

```sh
python3 scripts/security/scan.py secrets        # full Git history, gitleaks
python3 scripts/security/scan.py dependencies   # NuGet + npm
```

### Smoke tests (requires running Docker stack)

```sh
docker compose --profile tools build --builder default smoke
docker compose run --rm --no-deps smoke
docker compose down
docker compose up -d --wait
docker compose run --rm --no-deps smoke --verify-restart
```

---

## RAG Evaluation

The 30-case frozen evaluation dataset (`evaluation/golden-v1.json`) is run
against a real PostgreSQL instance. No paid API key is needed.

```sh
# Validate dataset integrity (no database needed)
dotnet run --no-build --project tools/IndustrialCopilot.Evaluation -- --validate

# Full deterministic evaluation run
$env:EVALUATION_POSTGRES = "Host=127.0.0.1;Port=5432;Database=maintenance_evaluation;Username=postgres;Password=<password>"
dotnet run --no-build --project tools/IndustrialCopilot.Evaluation -- --repeat
```

Or in Docker (no host .NET needed):

```sh
docker compose --profile evaluation build --builder default evaluation
docker compose --profile evaluation up -d --wait evaluation-db
docker compose run --rm --no-deps evaluation --validate
docker compose run --rm --no-deps evaluation --repeat
```

See [`docs/EVALUATION.md`](docs/EVALUATION.md) for the full metric table,
honest poor-score explanation, and CRLF/LF cross-platform limitation.

---

## Shutdown and Restart

```sh
# Stop containers — preserves all data and credentials
docker compose down

# Start again (seconds — no rebuild needed)
docker compose up -d --wait
```

**Do not use `docker compose down -v`** unless you intend to erase the database,
indexes, conversation history, and secret volumes. A fresh volume set requires
re-running `credentials` and `corpus`.

---

## Troubleshooting

**Port already in use:**
```sh
# Change the web port in .env:
WEB_PORT=18080
docker compose down
docker compose up -d --wait
```

**API not ready after `up -d --wait`:**
```sh
docker compose ps -a
docker compose logs migrate seed api worker web
```

**Credentials lost:**
If you ran `down -v`, the credential volumes were deleted. Run `up -d --wait`
again — `setup` will generate fresh credentials and a new database password.
Re-run `corpus` to re-ingest the assessment corpus.

**Frozen evaluation SHA256 mismatch:**
Never edit `evaluation/golden-v1.json`. Run `--validate` to confirm integrity.

---

## Architecture

The project follows Clean Architecture:
- **Domain** — entities, safety invariants, domain rules
- **Application** — use cases, agent orchestration, ports/interfaces
- **Infrastructure** — LLM adapters, pgvector, PostgreSQL, Docker packaging
- **API** — ASP.NET Core, HTTP endpoints, SSE streaming
- **Worker** — background job consumer, reconciliation
- **Angular** — production UI (untrusted boundary)

Three specialized agents (SymptomMatcher, DiagnosticSafetyPlanner,
WorkOrderGenerator) run in a Sequential Pipeline. Safety validation is
deterministic code outside the LLM. Human approval is mandatory before dispatch.

See [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) for C4 diagrams (L1–L3),
full sequence diagram, DFD with trust boundaries, ERD, and layer diagram.

See [`docs/SYSTEM-DESIGN.md`](docs/SYSTEM-DESIGN.md) for the target production
architecture (Part A) and the complete MVP gap table (Part B).

---

## Key Documentation

| Document | Purpose |
|---|---|
| [`docs/SYSTEM-DESIGN.md`](docs/SYSTEM-DESIGN.md) | Target architecture + MVP gap table |
| [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) | C4 diagrams, sequence, DFD, ERD |
| [`docs/DEPLOYMENT.md`](docs/DEPLOYMENT.md) | Full evaluator path + troubleshooting |
| [`docs/EVALUATION.md`](docs/EVALUATION.md) | FR-3 dataset, metrics, honest limitations |
| [`docs/SECURITY.md`](docs/SECURITY.md) | OWASP controls, threat model |
| [`docs/BRD.md`](docs/BRD.md) | Business requirements + traceability matrix |
| [`docs/AI-USAGE-LOG.md`](docs/AI-USAGE-LOG.md) | AI/human boundary per PR |
| [`docs/AGENTIC-WORKFLOW.md`](docs/AGENTIC-WORKFLOW.md) | Agentic coding mechanisms |
| [`docs/adr/`](docs/adr/) | Architecture Decision Records (ADR-001–014) |
| [`CONTRIBUTING.md`](CONTRIBUTING.md) | Branching, PR, test, security conventions |
| [`prompts/`](prompts/) | Versioned agent system prompt library |

---

## Related Guides

- [Deployment, evaluation and troubleshooting](docs/DEPLOYMENT.md)
- [Assessment corpus and ingestion](docs/CORPUS-INGESTION.md)
- [Reproducible bilingual demo](docs/DEMO-GUIDE.md)
- [Angular frontend setup](docs/FRONTEND-SETUP.md)
- [Host setup and configuration](docs/HOST-SETUP.md)
- [Bounded resilience and usage accounting](docs/RESILIENCE-AND-USAGE.md)
- [Teaching pack](teaching/README.md)

---

## Status

Implemented: three specialized agents, sequential orchestration, OpenAI/Ollama
ports and adapters, keyword/dense/hybrid retrieval over PostgreSQL/pgvector,
durable work orders and traces, deterministic safety, human approval, four trusted
tools, idempotent dispatch reservations, REST/SSE API, reconciliation Worker and
an Angular operations workspace for diagnosis, review, safety and dispatch.

T7 durable jobs use PostgreSQL submission, leases, progress events and safe replay
through the existing orchestrator. The Worker processes reasoning and separately
reconciles unresolved dispatch attempts. See
[ADR-008](docs/adr/ADR-008-durable-async-reasoning-jobs.md).

See [AI usage log](docs/AI-USAGE-LOG.md) for the full per-issue development log
and [AGENTIC-WORKFLOW.md](docs/AGENTIC-WORKFLOW.md) for agentic mechanisms.
