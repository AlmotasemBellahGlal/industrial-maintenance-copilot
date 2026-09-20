# System Design: Industrial Maintenance Copilot

**Variant:** D5 Industrial / Field Maintenance + T7 Durable Async Jobs
**Status:** MVP implemented for ITI Technical Instructor Assessment

This document is the primary design reference.
It has two clearly separated parts.

- **Part A** describes the unconstrained production/enterprise target architecture.
- **Part B** describes what is implemented now, why decisions were made under assessment
  constraints, and what the gap to production deployment looks like.

Read Part A to understand the intended system at scale.
Read Part B to understand what is actually running in this repository.

---

## Part A — Target Architecture (Unconstrained Production)

This section describes the system as it would be designed for a real industrial
organisation with availability, security, compliance and operational requirements —
unconstrained by the assessment time window or cost limits.

### A.1 High-Level Overview

The Industrial Maintenance Copilot is a safety-constrained agentic RAG platform.
At production scale it must handle:

- Concurrent technician sessions across multiple facilities.
- High-availability ingestion of large document corpuses.
- Durable multi-agent reasoning workflows that survive infrastructure failures.
- Strict human-in-the-loop approval gates with full audit trails.
- Regulatory and compliance requirements for maintenance documentation.
- Cost-controlled, observable LLM usage.

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                         Public Internet / VPN                                │
│                                                                             │
│   Browser clients          Mobile clients         ERP / CMMS systems        │
└──────────────┬──────────────────┬────────────────────────┬──────────────────┘
               │ HTTPS/TLS        │ HTTPS/TLS              │ mTLS / API key
               ▼                  ▼                        ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│                        API Gateway / CDN                                    │
│  WAF · Rate limiting · TLS termination · DDoS mitigation                    │
│  Auth token introspection · Request routing                                  │
└──────────────────────────────────┬──────────────────────────────────────────┘
                                   │
                    ┌──────────────┴──────────────┐
                    ▼                             ▼
         ┌──────────────────┐          ┌──────────────────┐
         │   API Service    │          │  Ingestion API   │
         │  (N replicas)    │          │  (separate tier) │
         │  REST + SSE      │          │  async pipeline  │
         └────────┬─────────┘          └────────┬─────────┘
                  │                             │
         ┌────────▼─────────┐          ┌────────▼─────────┐
         │  Message Broker  │◄─────────┤  Message Broker  │
         │  (durable queue) │          │  (ingestion lane)│
         └────────┬─────────┘          └──────────────────┘
                  │
        ┌─────────┼──────────┐
        ▼         ▼          ▼
 ┌──────────┐ ┌──────────┐ ┌──────────┐
 │ Worker-1 │ │ Worker-2 │ │ Worker-N │  (Autoscaled reasoning workers)
 └──────────┘ └──────────┘ └──────────┘
        │
        ├──► Managed PostgreSQL  (operational data)
        ├──► Managed Vector DB   (knowledge index)
        ├──► Object Storage      (raw documents)
        └──► LLM Provider        (hosted or on-premise)
```

### A.2 API Gateway

**Purpose:** Single ingress point enforcing security and traffic management before
any application code runs.

**Requirements:**
- TLS 1.3 termination with managed certificate lifecycle (ACM, Let's Encrypt, cert-manager).
- Web Application Firewall (OWASP CRS or equivalent) for injection, XSS and API abuse.
- Distributed rate limiting per authenticated actor — not per replica — so limits
  hold under horizontal scaling. Token bucket per actor stored in a shared cache
  (Redis or gateway-native).
- Authentication token introspection: JWT validation against the identity provider's
  JWKS endpoint; short-lived access tokens with refresh flow.
- Request routing to API service, ingestion service and health endpoints.
- DDoS mitigation at the edge (CloudFront, Cloudflare, Azure Front Door).

**Why not in MVP:** The MVP runs on a loopback port with a bearer-token credential
check inside the application. This is sufficient for local assessment evaluation
but not suitable for internet exposure.

### A.3 Identity and Authentication Architecture

**Production target:**

- **Identity provider:** Dedicated OIDC/OAuth 2.0 IdP (Keycloak, Azure AD B2B,
  AWS Cognito, Auth0) with short-lived signed JWTs.
- **Roles and claims:** `maintenance:technician`, `maintenance:supervisor`, and
  equipment-scope claims in the access token.
- **MFA:** Required for supervisor approval actions.
- **Service-to-service:** mTLS certificates or IAM-signed requests between Worker,
  API, message broker and database.
- **Token rotation:** Short-lived access tokens (15 min), refresh tokens (8 hours),
  background token refresh in the UI.
- **Audit logging:** Every authentication event forwarded to the SIEM.

**Why not in MVP:** The application uses a configured bearer-credential scheme
(`HostAuthentication`) that validates against a fixed set of hashed tokens in
startup configuration. This is intentional for a local demo and clearly documented
as not production identity management. Role enforcement (`read,start,approve,verify,
dispatch,ingest`) is fully server-side; the mechanism differs from production, not
the enforcement model.

### A.4 Secrets Management

**Production target:**

- All secrets (database passwords, LLM API keys, certificate private keys, OIDC
  client secrets) stored in a dedicated secrets manager (HashiCorp Vault, AWS
  Secrets Manager, Azure Key Vault, GCP Secret Manager).
- Application retrieves secrets at startup via the secrets manager SDK; secrets
  are never stored in environment variables committed to CI/CD pipelines.
- Secret rotation: automated for database passwords (short-lived dynamic credentials
  issued per service); manual with automated re-deploy for LLM API keys.
- No secrets in container images, Kubernetes YAML manifests, or source control.
- CI/CD uses OIDC-based identity federation to retrieve short-lived deployment
  credentials; no long-lived CI tokens.

**Why not in MVP:** Docker Compose generates random credentials at runtime via the
`setup` container into named volumes (`app-secrets`, `db-secret`). Credentials are
never committed to source control; the `.env.example` has no real keys. This
provides equivalent isolation for a local demo but not the rotation, auditing or
HSM-backed key management of a secrets manager.

### A.5 Durable Message Broker

**Production target:**

- Dedicated durable message queue: AWS SQS + DLQ, Azure Service Bus, RabbitMQ
  (clustered) or Apache Kafka (for event sourcing variant).
- Guaranteed-at-least-once delivery with idempotent consumers.
- Dead-letter queue for failed reasoning jobs with alerting.
- Message ordering within a single equipment workflow (partition by equipment ID
  on Kafka; FIFO queues on SQS).
- Maximum message visibility timeout aligned to the longest expected reasoning job.
- Independent scaling of producers (API) and consumers (Worker) via queue depth
  autoscaling metric.

**Why not in MVP:** The reasoning job queue is implemented using a PostgreSQL table
(`operations.reasoning_jobs`) with lease-based optimistic locking, discovery indexes
and bounded attempt counts. This was deliberately chosen for the assessment because
it eliminates an operational dependency while preserving the T7 durability properties
(survives restart, idempotent, cancellable, resumable). See ADR-008. For production,
a dedicated broker decouples API and Worker scaling and provides better observability.

### A.6 Horizontally Scalable Workers

**Production target:**

- Worker pods deployed as Kubernetes Deployment with HPA based on queue depth.
- Each Worker pod: independent, stateless, claims jobs via atomic lease acquisition.
- At-most-N concurrent reasoning jobs per pod (CPU/memory budget for LLM context
  windows).
- Graceful shutdown on SIGTERM: complete or safely abandon current job before
  termination (lease timeout handles abandonment automatically).
- Pod disruption budgets to prevent simultaneous eviction during rolling deployments.
- Separate Worker deployment for reasoning vs. reconciliation (different scaling
  characteristics).

**Why not in MVP:** Single Worker process per Compose project. The lease-based
architecture already supports multiple concurrent workers against the same database
without coordination; adding replicas is an operational, not architectural, change.

### A.7 Autoscaling

**Production target:**

- API: HPA on request latency (P99) and RPS. Minimum 2 replicas for HA.
- Worker: HPA on PostgreSQL queue depth (custom metric exposed via Prometheus
  adapter). Scale-to-zero acceptable for batch ingestion workers.
- Ingestion pipeline: separate autoscaled deployment to avoid contention with
  real-time reasoning workers.
- Database: read replicas for query-heavy reporting workloads; primary handles
  all writes.

**Why not in MVP:** Single API and Worker process. Stateless design means scaling
is an operational decision without code changes to the application.

### A.8 Caching

**Production target:**

- **Embedding cache:** Short-lived per-session cache for repeated identical queries
  to avoid re-computing embeddings on the same document/revision combination.
- **API response cache:** Read-through cache (Redis) for `/health/ready` and
  equipment/manual metadata endpoints. Never cache user-scoped or approval state.
- **CDN static assets:** Angular production build served from CDN with long-lived
  cache headers and immutable content hashes.
- **No LLM response caching:** Reasoning outputs must never be served from a cache
  because they depend on the current approved corpus state and must be attributable
  to a specific execution.

**Why not in MVP:** No caching layer. The deterministic test provider makes caching
irrelevant for assessment. Production embedding computation would benefit from a
short-lived cache.

### A.9 Managed PostgreSQL

**Production target:**

- Fully managed PostgreSQL with pgvector: AWS Aurora PostgreSQL with pgvector
  extension, Azure Database for PostgreSQL Flexible Server, or Google Cloud SQL.
- Multi-AZ deployment with automatic failover (RPO < 1 min, RTO < 30 sec).
- Automated daily backups with point-in-time recovery to 5-minute granularity.
- Encryption at rest (AES-256, managed keys in KMS).
- Private VPC endpoint; no public internet exposure.
- Separate read replicas for reporting and evaluation workloads.
- Connection pooling via PgBouncer to handle Worker pod fan-out.
- Automated minor version upgrades; major version migration with blue/green.

**Why not in MVP:** Single PostgreSQL container (`pgvector/pgvector:pg17`, digest-
pinned) in Docker Compose. Data persists in a named volume (`pg-data`). No replication,
automated backup, or HA. Suitable for local evaluation; not for production data.

### A.10 Managed Vector Database (Production Architecture)

**Production target — Option 1 (pgvector at scale):**
- Retain pgvector on managed PostgreSQL with table partitioning by embedding profile
  and HNSW indexes for sub-millisecond nearest-neighbour search at millions of chunks.
- Justified when: operational simplicity, transactional consistency with relational
  data, and avoidance of a separate data plane are priorities.
- Limitations: does not match Pinecone/Weaviate ANN performance at very large corpus
  sizes; requires DBA expertise to tune HNSW index parameters.

**Production target — Option 2 (dedicated vector store):**
- Dedicated managed vector database: Pinecone Serverless, Weaviate Cloud, Qdrant Cloud.
- Separate vector writes (ingestion) from vector reads (retrieval) for independent
  scaling.
- Justified when: corpus exceeds ~10M chunks, sub-5ms P99 retrieval at high concurrency
  is required, or multi-tenancy isolation is a compliance requirement.
- Additional complexity: two data planes to synchronise; citation traceability must
  be maintained across the relational and vector stores.

**Why not in MVP:** pgvector on the demo PostgreSQL container. Adequate for the
31-document / 150-page assessment corpus. The `IRetrievalService` / `IKnowledgeIndex`
abstraction supports swapping to a dedicated store without touching Application logic.

### A.11 Object/Document Storage

**Production target:**

- Raw ingested documents stored in object storage (AWS S3, Azure Blob Storage,
  GCS) with versioning enabled.
- Immutable document objects: a new revision creates a new object; originals are
  never overwritten.
- Access via pre-signed URLs with short expiry; no public read access.
- Virus scanning on upload via storage event triggers.
- Lifecycle policy: transition to cold storage after 1 year; retention minimum
  per maintenance regulation (e.g. ISO 9001 recommends 7 years).
- Documents processed from storage by the ingestion pipeline; raw bytes not stored
  in the database.

**Why not in MVP:** Documents submitted via HTTP body to the API and processed
in-memory. Raw bytes are not persisted to a separate store. The `IDocumentProcessor`
abstraction isolates this from Application logic; adding an object-storage adapter
is an Infrastructure change.

### A.12 Observability Stack

**Production target:**

- **Distributed tracing:** OpenTelemetry SDK in API and Worker, exporting to Jaeger
  or AWS X-Ray. Trace context propagated through message broker headers and PostgreSQL
  correlation IDs.
- **Structured logging:** JSON logs shipped to Elasticsearch/OpenSearch or CloudWatch
  Logs. Log level configurable per component without restart. PII fields excluded.
- **Metrics:** Prometheus metrics scraped from API and Worker sidecars. Grafana
  dashboards for reasoning job throughput, LLM token usage, retrieval latency,
  approval gate latency, error rates.
- **Alerting:** PagerDuty/OpsGenie integration. Alerts on: reasoning job dead-letter
  queue depth > 0, P99 API latency > 2s, LLM provider error rate > 5%, database
  connection pool exhaustion, disk space < 20%.
- **Cost monitoring:** LLM token usage aggregated per actor, equipment, and time
  window. Cost attribution dashboard with budget alerts.

**Why not in MVP:** Application-level tracing is implemented in `WorkflowTrace` and
`PostgresRunTraceStore` — every agent step, tool call, retrieval, approval, and LLM
call is recorded in the database and queryable via `/api/traces/{executionId}`. LLM
token usage and cost are recorded in `operations.llm_usage`. This provides the
observability data required for the assessment. Production would also export to an
external observability platform.

### A.13 CI/CD and Environment Separation

**Production target:**

```
Git push → CI (test / lint / security scan / Docker build)
         → dev namespace (auto-deploy on merge to main)
         → staging namespace (auto-deploy on release tag, requires E2E green)
         → production namespace (manual approval gate, canary deployment,
                                  automated rollback on error rate spike)
```

- Three named environments: dev, staging, production.
- No production deployment without passing: unit tests, integration tests,
  E2E browser tests, secret scan, dependency vulnerability scan.
- Canary deployment: 5% traffic to new version, promote after 15 min with no
  error rate increase.
- Automated rollback: triggered by Prometheus alert on error rate or latency
  regression within 30 min of deployment.
- Infrastructure as Code: all cloud resources defined in Terraform or CDK;
  no click-ops for production environments.
- Git branch protection on `main` with required CI status checks and at least
  one human review.

**Why not in MVP:** Single CI pipeline (`.github/workflows/ci.yml`) with four jobs:
packaging, security, frontend, validate. Deploys to a local Docker Compose stack.
No staging/production namespaces. This meets the assessment requirement for CI on
every PR.

### A.14 Backup and Disaster Recovery

**Production target:**

- **RTO (Recovery Time Objective):** < 4 hours for full recovery from total region failure.
- **RPO (Recovery Point Objective):** < 1 hour of data loss acceptable.
- Daily automated PostgreSQL backups to a separate region with cross-region replication.
- Vector store backups: daily snapshot of all embeddings with revision metadata.
- Document store: S3 cross-region replication enabled.
- DR runbook: documented and tested quarterly.
- Backup restore tested monthly (automated restore-and-verify job).
- Secret backup: secrets manager has automatic replication to a secondary region.

**Why not in MVP:** No automated backup. The Docker Compose `down` command preserves
the `pg-data` named volume; `-v` destroys it. Users are explicitly warned not to
use `-v`. For a real deployment, the first recovery exercise would be a prerequisite
for production go-live.

### A.15 High Availability

**Production target:**

- API: minimum 2 replicas across 2 availability zones. Active-active.
- Worker: minimum 2 replicas; lease-based exclusion prevents duplicate processing.
- Database: multi-AZ synchronous replication. Automatic failover in < 30 sec.
- Zero-downtime deployments via rolling update with pod disruption budgets.
- Circuit breakers on LLM provider calls (Polly / Resilience4j equivalent);
  configured fallback to `IPlainRagFallback` when provider is degraded.

**Why not in MVP:** Single-instance everything. The Worker's lease-based architecture
already supports HA without code changes; the API is stateless; adding replicas is
purely operational.

### A.16 Certificate / Key Rotation

**Production target:**

- TLS certificates: automated rotation via cert-manager or ACM, minimum 90-day
  validity, renewed at 60 days.
- Database credentials: rotated every 30 days via secrets manager dynamic secrets.
- LLM API keys: rotated every 90 days; overlap period of 48 hours during rotation.
- JWT signing keys: JWKS rotation with 24-hour overlap window.
- Internal mTLS certificates: 7-day validity, automated renewal.

**Why not in MVP:** Compose `setup` container generates a self-signed certificate
valid for 1 year. Demo credentials are static until volumes are deleted and
recreated. DEPLOYMENT.md notes: "The demo certificate is valid for one year;
long-lived deployments need managed certificate rotation."

### A.17 Deployment Strategy

**Production target:**

1. Build and tag Docker images with immutable content digest.
2. Push to private container registry (ECR, ACR, GCR).
3. Update Kubernetes Deployment manifest with new image tag via GitOps.
4. Rolling update with `maxSurge=1`, `maxUnavailable=0`.
5. Health check gate: pods must pass `/health/ready` before traffic is shifted.
6. 15-minute canary: 5% production traffic to new version.
7. Automated rollback if Prometheus error-rate alert fires within 30 min.
8. Manual promotion of canary to 100% after stability confirmed.

**Why not in MVP:** Docker Compose `up -d --wait` with health checks. The existing
`/health/live` and `/health/ready` endpoints would integrate directly with
Kubernetes liveness and readiness probes without code changes.

### A.18 Cost Model at Realistic Scale

The following estimates assume a medium-sized industrial maintenance operation
(50 technicians, 5 supervisors, 500 equipment items, 10,000-page corpus).

| Component | Monthly cost estimate | Notes |
|---|---|---|
| Managed PostgreSQL (db.r6g.large, multi-AZ) | ~$300 | AWS Aurora PostgreSQL |
| ECS/EKS for API + Worker (2+2 replicas) | ~$200 | Fargate pricing |
| ALB + WAF | ~$50 | Per-rule WAF pricing |
| OpenAI API (GPT-4o) | ~$500–$2,000 | Highly usage-dependent |
| Embedding (text-embedding-3-small) | ~$50 | ~10M tokens/month at corpus size |
| S3 document storage | ~$5 | ~50 GB at typical corpus density |
| Secrets Manager | ~$5 | ~10 secrets × $0.40/month |
| CloudWatch / observability | ~$50 | Logs + metrics |
| **Total** | **~$1,100–$2,600/month** | Excluding data transfer |

LLM costs dominate at scale. The biggest optimisation lever is caching, prompt
compression and model-tier selection (GPT-4o-mini for SymptomMatcher where latency
is more important than reasoning depth).

The demo uses a deterministic provider with zero inference cost. A production
Ollama deployment on a GPU instance (~$0.90/hr for g4dn.xlarge) provides a
fixed-cost alternative with lower quality.

---

## Part B — Implemented MVP

This section documents what actually exists in this repository, the decisions made
under assessment constraints, and the honest gap to production.

### B.1 What Is Implemented

The following runtime components are all running in the `maintenance-proof39-final`
Docker Compose project (verified by CI `packaging` job):

| Component | Implementation | Location |
|---|---|---|
| Angular frontend | Production build served by Nginx | `src/IndustrialCopilot.Web` |
| ASP.NET Core API | Loopback HTTPS with self-signed cert | `src/IndustrialCopilot.Api` |
| Background Worker | Hosted service, lease-based job consumer | `src/IndustrialCopilot.Worker` |
| PostgreSQL + pgvector | Docker image `pgvector/pgvector:pg17` (digest-pinned) | `compose.yaml` |
| Three specialized agents | SymptomMatcher, DiagnosticSafetyPlanner, WorkOrderGenerator | `src/IndustrialCopilot.Application/Reasoning/` |
| Sequential orchestrator | `MaintenanceOrchestrator` Sequential Pipeline | `src/IndustrialCopilot.Application/Reasoning/` |
| Hybrid RAG | Keyword + Dense + RRF hybrid over pgvector | `src/IndustrialCopilot.Infrastructure/Knowledge/` |
| OpenAI provider | `OpenAiLlmProvider` | `src/IndustrialCopilot.Infrastructure/AI/` |
| Ollama provider | `OllamaLlmProvider` | `src/IndustrialCopilot.Infrastructure/AI/` |
| Deterministic test provider | `DemoProvider` (no paid key needed) | `tools/IndustrialCopilot.Demo/` |
| T7 durable jobs | PostgreSQL-backed, lease-based, idempotent | `src/IndustrialCopilot.Infrastructure/Operations/` |
| Human approval gate | Server-enforced, audit-trailed | Domain + `PostgresWorkOrderApprovalService` |
| Deterministic safety policy | `ISafetyPolicy`, double-assessment in orchestrator | `src/IndustrialCopilot.Application/` |
| Authentication | Bearer token, constant-time compare, equipment scope | `src/IndustrialCopilot.Api/ApiSecurity.cs` |
| Authorization | Role-based permissions, server-enforced | `MaintenanceEndpoints.Permit` |
| Conversation history | Persisted in PostgreSQL, citation-exact | `PostgresConversationStore` |
| LLM usage accounting | Per-call, owner-scoped, cost-optional | `PostgresLlmUsageStore`, `AccountedLlmProvider` |
| Workflow traces | Per-step, correlatable, inspectable via API | `PostgresRunTraceStore` |
| Assessment corpus | 31 synthetic docs / 150 PDF pages | `tools/IndustrialCopilot.Corpus/` |
| FR-3 evaluation | 30-case golden dataset, deterministic, reproducible | `tools/IndustrialCopilot.Evaluation/` |
| Docker packaging | Source-built, no host SDK, CI-proven | `Dockerfile`, `compose.yaml` |

### B.2 Gap Table

Every row is grounded in repository evidence.

| Target component | Implemented? | Current implementation | Why deferred | Interim mitigation | Risk accepted | Approx engineering effort to close | Approx operational/cost impact |
|---|---|---|---|---|---|---|---|
| API gateway | ✗ No | Application-layer bearer token check in `HostAuthentication` | Out of scope for assessment; no internet-facing deployment | Rate limiter in `ApiSecurity.cs` limits per-actor mutations; loopback-only port published | Medium — any internet exposure requires adding a gateway layer | 2–3 weeks (Nginx gateway + WAF rules + CDN config) | ~$50–150/month (ALB + WAF) |
| Distributed rate limiting | Partial | Per-actor fixed-window rate limiter in `ApiSecurity.cs` using ASP.NET Core `RateLimiter`; does not survive API replica restarts | Single-replica MVP; distributed state not needed | Per-process rate limiting is correct for the demo load | Low for MVP; medium when scaling to ≥2 replicas | 1 week (Redis-backed counter or gateway rate limiting) | Included in API gateway cost above |
| OIDC/MFA identity | ✗ No | Configured bearer credentials in startup config (`Authentication:Credentials` section) | Assessment requires no live cloud dependency; local demo only | Credentials are random 256-bit hex tokens, never committed, never logged; role enforcement is full server-side | High if exposed to internet without gateway; acceptable for local demo | 2 weeks (IdP integration, JWT middleware, MFA) | ~$0–50/month (Cognito free tier; Auth0 pricing at low user count) |
| Secrets manager | Partial | Docker Compose `setup` container generates secrets into named volumes; never in images or source | No cloud dependencies for assessment | Random generation, volume isolation, `.dockerignore` excludes `.env.*` | Low for local demo; high for production | 1 week (Vault/AWS SM integration; secret rotation scripts) | ~$5/month |
| Dedicated message broker | ✗ No | PostgreSQL table `operations.reasoning_jobs` with lease-based discovery (ADR-008) | Eliminates operational dependency; T7 requirements fully met with PostgreSQL | Atomic lease acquisition, bounded discovery, idempotent job processing; Workers scale horizontally against same DB | Low — PostgreSQL queue meets T7 requirements; at very high throughput a dedicated broker would reduce DB load | 2 weeks (SQS/RabbitMQ adapter implementing existing IJobQueue abstraction) | ~$10–50/month |
| Horizontally scalable Workers | Partial (architecture ready) | Single Worker process in Compose | Single-process is sufficient for assessment demo | Lease-based exclusion already supports N workers; adding replicas is operational | Low | 1 day to configure; no code change needed | Included in compute costs |
| Autoscaling | ✗ No | Fixed single-instance deployment | No Kubernetes/cloud for assessment | Health probes ready for Kubernetes liveness/readiness | Low | 1 week (K8s HPA config + custom metrics) | Included in managed cluster costs |
| Redis cache | ✗ No | No caching | Unnecessary for demo corpus size and load | N/A | Low | 1 week (embedding cache; session cache for repeated queries) | ~$20–50/month |
| Managed PostgreSQL | ✗ No | `pgvector/pgvector:pg17` container in Compose with named volume | No cloud for assessment | Single-container PostgreSQL meets all functional requirements for demo; data persists in named volume | High for production — no HA, no automated backup | 1 day (connection string change + TF config) | ~$200–400/month |
| Dedicated vector store | ✗ No | pgvector on the same PostgreSQL instance | pgvector meets assessment corpus scale; adds dependency otherwise | `IRetrievalService` abstraction allows swapping without Application changes | Low at assessment corpus size (150 pages / ~500 chunks) | 1–2 weeks (adapter implementation) | ~$70–300/month (Pinecone serverless) |
| Object storage | ✗ No | Documents processed in-memory from HTTP POST body; raw bytes not persisted | No cloud for assessment | `IDocumentProcessor` abstraction isolates this; retrieval uses chunk metadata, not raw docs | Low — raw documents are not required for retrieval after ingestion; re-ingestion is idempotent | 1 week (S3 adapter + lifecycle policy) | ~$5/month |
| External observability (OTEL) | Partial | Application-level tracing in `WorkflowTrace` / `PostgresRunTraceStore`; LLM usage in `llm_usage` table; health endpoints at `/health/live` and `/health/ready` | No external platform for assessment | All trace and usage data is queryable via API; CI smoke verifies trace and usage records | Low for assessment; medium for production — no alerting on anomalies | 1 week (OTEL SDK + exporter to Jaeger/CloudWatch) | ~$50/month |
| CI/CD environments (dev/staging/prod) | Partial | Single CI pipeline, Docker Compose packaging proof | Assessment is a local demo; no cloud environments | All code changes pass CI on PR before merge; Docker packaging proven in `packaging` CI job | Medium — no staging gate before production concept | 2 weeks (GitOps repo + K8s namespaces + Argo CD) | ~$100/month (compute for staging) |
| Automated backup | ✗ No | `docker compose down` preserves volumes; `-v` destroys them | Local demo | README explicitly warns against `down -v` | High if used with production data | 1 day (pg_dump cron + S3 upload) | ~$2/month |
| HA / multi-AZ | ✗ No | Single-instance all services | Assessment is local | Stateless API design; lease-based Worker already supports HA | High for production SLA | Operational config (no code change) | Included in managed service costs |
| Certificate rotation | Partial | Self-signed certificate valid 1 year, generated by `setup` container into `app-secrets` volume | Annual rotation acceptable for demo; DEPLOYMENT.md notes the limitation | Certificate is never committed; generated at first startup | Low for demo; medium for production | 1 week (cert-manager + rotation automation) | ~$0 (cert-manager is open source) |
| Canary deployment | ✗ No | `docker compose up -d --wait` rolling replace | No K8s for assessment | Health checks gate startup; failed one-shot containers block dependents | Low | Operational config | Included in cluster costs |
| Disaster recovery plan | ✗ No | No formal DR plan | Out of scope for assessment | Data is reproducible for demo (corpus ingestion is idempotent; credentials regenerate on volume delete) | High for production | 2 weeks (runbook + test exercise) | Operational cost |
| SIEM / security event logging | ✗ No | Authentication failures logged at INFO level | No cloud for assessment | `HostAuthentication` returns 401 without leaking information; rate limiter bounds brute-force attempts | Low for demo | 1 week (structured auth event logging + SIEM integration) | ~$50/month |

### B.3 Significant Design Decisions

#### B.3.1 PostgreSQL as the Job Queue

**Decision:** Use a PostgreSQL table with atomic lease acquisition instead of a
dedicated message broker (SQS, RabbitMQ).

**Alternatives considered:**
- RabbitMQ: adds operational dependency, requires persistent storage for queues,
  visibility timeout semantics differ from the lease model needed for bounded recovery.
- AWS SQS: cloud dependency incompatible with local-first assessment requirement.
- In-memory Channel: loses jobs on process restart — violates T7 requirement.

**Why PostgreSQL:** A single infrastructure dependency (PostgreSQL is already
required for relational state), atomic lease claims prevent duplicate processing,
and the Worker recovery proof is straightforward to demonstrate without a broker.
The `IJobQueue` abstraction means swapping to a dedicated broker is an Infrastructure
change that does not touch Application or Domain code.

**Known limitation:** Under very high throughput, the reasoning_jobs discovery index
can become a bottleneck. At the assessment's load (one job at a time in the demo)
this is not observable.

#### B.3.2 Inline System Prompts vs. External Prompt Artifacts

**Decision (current):** Agent system prompts are C# string literal verbatim strings
inside the agent classes (`SymptomMatcherAgent.cs`, `DiagnosticSafetyPlannerAgent.cs`,
`WorkOrderGeneratorAgent.cs`). The authoritative versioned content is maintained in
the `prompts/` directory as Markdown files (added in Issue #41).

**Alternatives considered:**
- Pure file-path loading: `File.ReadAllText(path)` — breaks published/container
  builds because relative paths do not resolve after publish.
- Assembly embedded resources: reliable but requires MSBuild EmbeddedResource
  entries and `Assembly.GetManifestResourceStream` — adds boilerplate but guarantees
  portability.
- Environment variable override: too fragile; makes container verification complex.

**Chosen approach (Issue #41):** The `prompts/` directory contains canonical versioned
Markdown prompt source files. The C# agent code is the runtime source of truth for
the prompt text actually sent to the LLM; prompt content is kept identical between
`prompts/` files and code via a prompt contract test that reads the committed files
and compares to the runtime value. This ensures: Docker packaging works without
path dependencies, prompts are reviewable as diffs, version history is visible in
git blame, and the contract test fails on divergence.

#### B.3.3 pgvector Dimension Strategy

**Decision:** Use deterministic 32-dimensional lexical hash embeddings
(`synthetic-lexical-v1`) for the demo/assessment corpus.

**Rationale:** Enables fully deterministic retrieval and evaluation without a paid
embedding model. The `IEmbeddingProvider` abstraction supports real embedding models
(OpenAI `text-embedding-3-small` with 1,536 or 3,072 dimensions; Ollama with
configurable dimensions) without changing retrieval logic.

**Known limitation:** The deterministic embeddings have no semantic understanding.
The FR-3 evaluation honestly reports 0% keyword hit and 18% hybrid hit — these
scores reflect the intentionally narrow test provider, not a claim about production
semantic retrieval quality.

#### B.3.4 Clean Architecture Boundary Enforcement

**Decision:** Project references enforce the Clean Architecture dependency direction
(Domain ← Application ← Infrastructure ← API/Worker). Domain and Application have
no direct dependency on LLM SDKs, database drivers, or web frameworks.

**Consequence:** Every external technology integration is an Infrastructure adapter
behind an Application port. This makes the gap-closing work in B.2 above predictable:
swapping the job queue, vector store, or LLM provider is an Infrastructure-layer
change that does not touch business rules or agent orchestration.

### B.4 What Is Genuinely Not Implemented

The following are explicitly out of scope for the assessment MVP and are **not**
presented as implemented anywhere in this document or the codebase:

- Direct control of physical industrial equipment.
- Integration with real factory PLC, SCADA, or IoT systems.
- ERP or CMMS integration (the dispatch adapter writes to a PostgreSQL inbox,
  not a real ERP system).
- Predictive maintenance from live sensor data.
- Production-scale multi-region deployment.
- Native mobile applications.
- Automated spare-parts management.
- Fully autonomous maintenance decisions (human approval remains mandatory).

### B.5 Known Limitations and Accepted Risks

| Limitation | Honest assessment | Mitigation in place |
|---|---|---|
| FR-3 retrieval quality (0% keyword, 18% hybrid) | Reflects the intentionally narrow deterministic provider, not production LLM quality | Documented in EVALUATION.md; evaluation is reproducible with a real provider |
| CRLF/LF difference in citation locators across OS | Linux container extracts LF; frozen Windows baseline has CRLF; snippet bytes differ cross-platform | Documented in EVALUATION.md and DEPLOYMENT.md; baseline not modified |
| No automated backup | Data loss if host machine fails | Volumes survive `down`; `-v` flag explicitly warned against |
| Single-region / single-node | No HA, no failover | Acceptable for local assessment demo |
| Demo certificate valid 1 year | Long-lived deployments need rotation | Documented in DEPLOYMENT.md |
| No semantic embedding model in demo | Retrieval quality reflects hash embeddings, not real vectors | Provider abstraction supports OpenAI/Ollama with real models |
| Bearer token credentials (not OIDC) | Not suitable for production identity management | Documented clearly; role enforcement is correct server-side |
