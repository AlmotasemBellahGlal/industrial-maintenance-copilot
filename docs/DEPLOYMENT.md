# Docker Compose evaluator deployment

**Validation status (Issue #39, 2026-09-18): complete.** All source-built Docker
images passed genuine Docker-only fresh-clone acceptance on clean volumes, with no
host .NET SDK, host Node, host PostgreSQL or host-published assemblies involved.
Corpus (31 docs/150 PDF pages), idempotent re-ingestion, bilingual Ask/streaming/
citations, D5 approval/safety/dispatch workflows, T7 Worker crash-and-recovery,
persistence across restart, deterministic evaluation (byte-identical to frozen FR-3
baseline), and all smoke assertions passed. PR submitted; see branch
`codex/deployment-compose-readiness`.

This packages the existing MVP, not a new implementation of ingestion, agents,
approval or dispatch. The default is explicitly a **local synthetic demonstration**.
Docker Desktop/Linux containers and Git are the only evaluator prerequisites.
Image downloads require network access; first build also restores NuGet/npm
dependencies. Allow several GB for images/build cache/database volumes. Do not
start builds on a nearly full Docker storage drive.

## Packaging audit against merged main

| Area | Existing capability / packaging gap addressed |
| --- | --- |
| Public clone | Source available; clean checkout previously needed host SDKs/configuration |
| Environment | Existing validated host settings; added documented Compose variables |
| PostgreSQL/pgvector | Existing adapters; added private pinned service and volume |
| Migrations | Existing migrations through 007; ordered one-shot runner |
| API | Existing host; published runtime and local-demo container entry point |
| Worker | Existing reasoning/reconciliation; independent running container |
| Angular | Existing UI/dev server; production build and same-origin Nginx |
| Corpus/seed | Existing FR-1 generator/pipeline; container operator commands |
| Demo identities | Existing role semantics; installation-specific random persistent tokens |
| Readiness | Existing API probes; container probes/dependency ordering |
| Persistence | Existing PostgreSQL schemas; named data/secret volumes |
| No-paid-key demo | Existing deterministic provider; opt-in container registration |
| Restart | Existing T7 replay/history/usage; packaged restart/crash smoke |
| README | Host-only setup; prominent Docker-only evaluator path |

## Start from a clone

Use the README commands. `.env.example` contains every configurable Compose
variable; defaults need no API key. `docker compose config --quiet` validates the
configuration without printing optional keys. Build `api web` first: all .NET
services share the same demo image, so there is one restore/publish cache.
`--builder default` selects Docker's built-in builder without changing machine
settings or downloading a separate BuildKit daemon.
If Docker Desktop selects a different builder or tries to bootstrap BuildKit despite
that flag, use `docker --context default compose build --builder default api web`
(the same prefix applies to the optional build commands). This selects the context
for that command only; it does not change Docker Desktop settings.

`docker compose up -d --wait` orders setup → healthy PostgreSQL → migrations →
canonical ingestion → healthy API → Worker/web. A failed one-shot prerequisite
prevents dependent startup. Inspect `docker compose ps -a` and
`docker compose logs migrate seed api worker web` when startup fails. Do not post
credentials, `.env`, configuration files or private volume contents in bug reports.

Only `127.0.0.1:8080` is published. Change `WEB_PORT` for a port collision. The
browser uses same-origin `/api`; PostgreSQL and API have no host ports. Nginx
serves a production Angular build, falls back to `index.html` for client routes,
and proxies unbuffered SSE over certificate-verified internal HTTPS. No permissive
CORS or API HTTPS bypass is added. Loopback HTTP is for local evaluation only;
an Internet deployment requires externally managed TLS, identities and secrets.

## Components and persistence

| Component | Responsibility |
| --- | --- |
| setup (one-shot) | Generate random database password, demo role tokens and internal TLS certificate; preserve existing volumes |
| db | PostgreSQL 17 + pgvector, persistent `pg-data` |
| migrate (one-shot) | Existing knowledge and operations migrations through 007 (usage accounting) |
| seed (one-shot) | Existing ingestion service, canonical synthetic pump manual |
| api | Existing API host with opt-in deterministic provider |
| worker | Existing durable reasoning worker and independent dispatch reconciliation |
| web | Non-root Nginx + production Angular; public certificate only |
| credentials / corpus | Explicit operator tools, never public endpoints |
| smoke | Existing HTTP smoke journeys plus packaged restart/recovery checks |
| evaluation-db / evaluation | Optional isolated FR-3 database and existing evaluator |

`app-secrets` contains API/Worker configuration, role tokens and TLS private key;
`db-secret` contains the PostgreSQL password; `public-ca` contains only the public
certificate. These are Docker named volumes, not files in the clone or image.
Docker administrators can access them: Docker is the local trust boundary.
Back up matching data **and** secret volumes together. Do not delete secrets while
keeping a database initialized with the old password. The demo certificate is
valid for one year; long-lived deployments need managed certificate rotation.

API, Worker and web run non-root with read-only root filesystems, writable `/tmp`,
dropped capabilities and no-new-privileges. Only initialization runs as root to
create volume contents; PostgreSQL uses its upstream initialization behavior.
No service is privileged and no Docker socket is mounted. Images are digest
pinned; npm uses its committed lockfile. Production-only API/Worker Docker targets
are available as `docker build --target api .` / `--target worker`; they require
the existing production host configuration and do not register demo providers.

## Demo roles and journeys

`docker compose run --rm --no-deps credentials` explicitly displays this
installation's random tokens. Setup logs never display them. In **Connection**:

* Technician (`demo-technician`): read/start scoped to the synthetic equipment.
  Cannot upload, approve, verify safety or dispatch; server rejects these actions.
* Supervisor (`demo-supervisor`): read/start/ingest/approve/verify/dispatch for that
  equipment. History remains actor-scoped, not shared simply by role.

Equipment: `11111111-1111-1111-1111-111111111111`.
Canonical document: `22222222-2222-2222-2222-222222222222`.
Revision: `33333333-3333-3333-3333-333333333333`.
Ask `pump vibration` / `اهتزاز المضخة`, then inspect exact citations and history.
Start a diagnosis for pump vibration/seal leakage. Review the proposed work order:
approval alone cannot dispatch it. The trusted safety verification path must also
satisfy every mandatory prerequisite. Approve/Reject/EditAndApprove retain their
existing revision and concurrency behavior. Dispatch creates an inbox ticket,
not physical equipment execution. Inspect trace and usage with the same actor.
Synthetic usage is explicitly unknown/nonbillable, never a fabricated token count.

بالعربية: افتح الرابط، اختر «الاتصال»، ثم أدخل مفتاح المشرف أو الفني الناتج من
أمر `credentials`. اختر العربية، واسأل عن اهتزاز المضخة مع معرفات الدليل أعلاه.
ابدأ تشخيصًا وراجع أمر العمل. موافقة المشرف وحدها لا تسمح بالإرسال: يجب التحقق
من متطلبات السلامة أولًا. أمر `down` العادي يحفظ البيانات؛ لا تستخدم `-v`.

## Corpus and evaluation

```sh
docker compose run --rm --no-deps corpus
docker compose run --rm --no-deps corpus corpus-status
docker compose run --rm --no-deps corpus
```

This invokes `AssessmentCorpus.Generate` and the existing `ManualIngestionService`:
31 synthetic manuals (PDF and text), at least 150 PDF pages. The status command
requires Completed for every revision. Re-ingestion replaces the same revision;
it does not append duplicate chunks. They are associated with the synthetic
training equipment for browsing, not newly authorized industrial procedures.
Only the existing reviewed canonical procedure grants deterministic safety scope.

```sh
docker compose --profile evaluation build --builder default evaluation
docker compose --profile evaluation up -d --wait evaluation-db
docker compose run --rm --no-deps evaluation --validate
docker compose run --rm --no-deps evaluation --repeat
```

The evaluator shares only its dedicated database network namespace so the
existing loopback/isolation guard remains unchanged. It does not touch the demo
database. Results persist in `evaluation-results` (`results.json`, `summary.md`).
The frozen dataset/baseline are not rewritten. This is deterministic evaluation,
not a claim of hosted-model quality.

The checked-in baseline was produced with Windows PDF-extraction line endings.
The existing extractor produces LF in Linux containers; the pipeline deliberately
preserves that extracted text. Aggregate metrics match the frozen baseline, but
some snippet line endings, scalar locators and derived chunk IDs differ from its
Windows evidence records. Repeated Linux runs are byte-identical. Do not claim
cross-platform byte identity or migrate an existing index by silently rewriting
its citations. All normal Compose runs use the same pinned Linux environment;
Git also fixes canonical demo text to LF across fresh host checkouts.

## Readiness, shutdown and recovery

`/health/live` describes process liveness; `/health/ready` checks database-backed
operational/knowledge dependencies. API health uses verified internal TLS;
web health checks the proxy's API readiness. Worker health checks its database
schema while its main process must remain alive. It is **not** proof of model
availability or job progress; smoke tests provide that proof.

`docker compose down` stops containers/network while retaining volumes. Start
again with `up -d --wait`, then run `smoke --verify-restart` after the first smoke.
History, ingestion/indexes and usage records must retain their identities.
To exercise a process crash with an in-flight synthetic model call:

```sh
docker compose stop worker
docker compose run -d --no-deps --name maintenance-recovery-worker -e DEMO_MODEL_DELAY_MS=30000 worker
docker compose run --rm --no-deps smoke --submit-recovery
docker rm -f maintenance-recovery-worker
docker compose start worker
docker compose run --rm --no-deps smoke --verify-recovery
```

The explicit temporary container name must be unused. This deliberately kills
only that test Worker. Recovery waits for lease expiry and safely replays an
attempt; it does not resume inside an interrupted model call. SSE disconnect
does not cancel durable jobs. Explicit cancellation is persisted; request-owned
Ask cancellation still propagates to generation. Approval and safety gates remain
separate from successful reasoning.

## Optional providers and limits

`DEMO_PROVIDER=Demo` is the tested no-paid-key default. OpenAi requires an optional
`OPENAI_API_KEY`, real `CHAT_MODEL`/`EMBEDDING_MODEL`, and appropriate embedding
profile/revision/dimensions. Ollama requires an already-running reachable service
and installed models (`OLLAMA_ENDPOINT`); Compose never downloads models.
Because `host.docker.internal` is not loopback inside the container, the existing
provider policy requires **HTTPS with a certificate trusted by the container**
(for example an existing TLS gateway). A stock HTTP Ollama listener is not
directly compatible with this path; configure a trusted endpoint rather than
disabling certificate validation or weakening the provider's transport policy.
The optional provider path still uses local demo identities; it is not production
deployment configuration. An embedding profile must identify a compatible space;
never reuse the synthetic profile for real vectors or silently switch dimensions
in an existing index. Use an explicitly separate Compose project/data set for a
different configuration. Live hosted/Ollama calls are not required or claimed by
the deterministic smoke. Authentication, safety and PDF extraction are reused.

## Validation

Container proof: build/up/credentials/corpus/smoke/restart/evaluation commands above.
Developer regression: `dotnet build`, `dotnet test` with `RAG_TEST_POSTGRES`,
frontend `npm run typecheck`, `npm test`, `npm run build`, `npm run test:e2e`,
`python scripts/security/scan.py dependencies`, `... secrets`, `git diff --check`.
Real browser tests accept `DEMO_WEB_URL`, `DEMO_API_BASE`, `DEMO_CREDENTIAL_FILE`,
`DEMO_TECHNICIAN_FILE` and `DEMO_E2E=1`; credentials remain outside source control.
