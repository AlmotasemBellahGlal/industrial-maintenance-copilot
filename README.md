# Industrial Maintenance Copilot

An agentic RAG platform for industrial field maintenance, built for the ITI Technical Instructor Assessment.

## Assigned Variant

- Domain: D5 — Industrial / Field Maintenance
- Mandatory Twist: T7 — Async Long-Running Jobs

## Project Goal

The system helps maintenance technicians identify relevant equipment manuals, retrieve grounded diagnostic guidance with citations, enforce safety prerequisites, and generate draft work orders that require supervisor approval before dispatch.

## Architecture

The project follows Clean Architecture principles with the following layers:

- Domain
- Application
- Infrastructure
- API
- Worker
- Angular Web client (untrusted)

## Status

Implemented: three specialized agents, sequential orchestration, OpenAI/Ollama
ports and adapters, keyword/dense/hybrid retrieval over PostgreSQL/pgvector,
durable work orders and traces, deterministic safety, human approval, four trusted
tools, idempotent dispatch reservations, REST/SSE API, reconciliation Worker and
an Angular operations workspace for diagnosis, review, safety and dispatch.

T7 durable jobs use PostgreSQL submission, leases, progress events and safe replay
through the existing orchestrator. The Worker processes reasoning and separately
reconciles unresolved dispatch attempts. See [ADR-008](docs/adr/ADR-008-durable-async-reasoning-jobs.md).
Legacy request-owned endpoints remain compatible; `/api/jobs` is the durable path. The demonstration dispatch adapter queues durable tickets, not ERP
work or physical execution.

## Build and run

Requires .NET 10. Database integration requires PostgreSQL with pgvector.

```sh
dotnet build
dotnet test
```

See [host setup, configuration and demo](docs/HOST-SETUP.md) before starting:

```sh
dotnet run --project src/IndustrialCopilot.Api
dotnet run --project src/IndustrialCopilot.Worker
```

Startup intentionally fails without reviewed procedures, selected provider/database
configuration and trusted host permissions. No credentials are included. Normal
tests use deterministic provider fakes; database tests use isolated PostgreSQL in
CI and skip locally unless `RAG_TEST_POSTGRES` is set.

For the browser workspace, see [Angular setup and demo](docs/FRONTEND-SETUP.md).
Use Node 26/npm 11, then `npm ci` and `npm start` in `src/IndustrialCopilot.Web`.
The dashboard uses labelled session activity, not invented server metrics.

See [architecture](docs/ARCHITECTURE.md), [API/Worker ADR](docs/adr/ADR-006-api-streaming-reconciliation.md)
and [AI usage log](docs/AI-USAGE-LOG.md).

## Reproducible bilingual demo

See [DEMO-GUIDE](docs/DEMO-GUIDE.md) for the synthetic pump scenario, real PostgreSQL/API/Worker/Angular startup, English/Arabic presentation boundaries, safety and approval smoke tests, and development-only reset instructions. The separate demo executable uses a deterministic LLM test double; live providers are optional.

## Assessment corpus and ingestion

See [CORPUS-INGESTION](docs/CORPUS-INGESTION.md) for the synthetic 31-document / 150-page corpus, UTF-8 and PDF pipeline, durable status, deterministic ingestion/retrieval smoke and operator commands.
