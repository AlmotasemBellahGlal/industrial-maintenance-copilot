# AI Usage Log

## 2026-09-13 — Issue #23 Angular operations frontend

### Delegated to AI
- Inspected merged PR #22 HTTP DTOs, authentication, SSE wire format, revision targets, safety preview/approval, verification, dispatch and trace endpoints.
- Created Issue #23 and its actual-number branch. Initially stopped because Codex UI UX Pro Max integration was missing, under the user's explicit no-install instruction. The human installed and verified CLI 2.15.0 integration and Python 3.14.6, then work resumed.
- Ran the skill's design-system generation and Master persistence workflow before UI implementation, plus Angular form/state and focused keyboard/error guidance searches. Adapted generated Swiss-style density/typography to operational safety states; rejected marketing hero, red generic CTA, invented metrics and scroll animation recommendations.
- Implemented standalone Angular with typed adapters, reactive forms, bounded request-owned SSE, session-only credential/activity state, real review/approval/verification/dispatch contracts, accessible dialogs, responsive layouts and locally bundled fonts.

### Human-approved boundaries
- The user required Angular, UI UX Pro Max, evidence-first presentation, separate human approval/safety gates, no fake backend data, and completion through PR without merging.
- The browser remains untrusted; the server supplies identity, permissions and authoritative requirements. No claim is made that the human reviewed every generated line or screen.

### AI mistakes and self-review corrections
- Corrected native form submission accidentally reloading the session, and nonreactive confirmation labels under Angular change detection, found by real browser tests.
- Corrected test locators to use accessible link names rather than hidden decorative numbering.
- Removed low-contrast transitional disabled-button colors, retained visible keyboard focus, and added reduced-motion/mobile dialog checks.
- Bound async review operations to their captured work order/generation; stale responses cannot overwrite a different route. Navigation dismisses pending confirmation and does not grant consent.
- Added exact preview invalidation, read-only requirement echo, conflict reload, no optimistic verification/approval, no automatic SSE POST replay and no automatic duplicate dispatch.
- A shell-based documentation write was automatically rejected; used structured file patches instead. No software installation permission was inferred from that rejection.

### Verification
- Ran Angular production build, strict TypeScript/template checks, focused unit tests, browser workflow tests, and axe/overflow checks at 375/768/1024/1440px. Inspected rendered desktop/mobile screenshots against the persisted master.
- Browser tests use actual API wire-contract fixtures; they do not claim live OpenAI/Ollama/ERP execution. Cancellation, fragmented/truncated SSE, duplicate progress, session bounds, safe literal snippets, 401/403/409 and distinct dispatch states are covered.
- Ran .NET build and full local regression suite: 476 pass, 38 PostgreSQL tests skip without an isolated server. CI retains real PostgreSQL coverage; final totals are recorded in the PR. Existing Microsoft.OpenApi NU1903 remains out of scope.
- No backend implementation changes or invented endpoints were required. Added CI frontend validation and setup/demo documentation. Local skill assets and test artifacts are not committed.

## 2026-09-13 — Issue #21 API, streaming and Worker

### Delegated to AI
- Codex inspected the merged Domain/Application/Infrastructure implementation and implemented the ASP.NET Core host, explicit HTTP DTOs, authenticated resource permissions, SSE observations and bounded reconciliation Worker.
- Suggested an exact executable-scope safety port and durable bounded discovery rather than moving industrial rules into endpoints or maintaining an in-memory recovery queue.
- Added host composition/configuration, explicit migration/ingestion commands, setup documentation and ADR-006.

### Human-reviewed decisions
- The user accepted the two discovered prerequisites: exact WorkOrderContent assessment and bounded discovery of unresolved dispatch attempts.
- The user explicitly authorized continued implementation, self-review, tests, documentation, commits, push and PR creation without merging. This entry does not claim a human line-by-line review of the resulting implementation.

### AI Mistakes / Corrections
- Rejected trusting client-supplied safety requirements, fabricating a DiagnosticPlan, duplicating deterministic safety rules in endpoints, and treating actorId as authorization.
- Edited approval uses a trusted preview plus comparison-only requirement echo; the Application recomputes and uses authoritative requirements at decision time.
- Corrected initial configuration deserialization to use explicit configuration DTOs rather than assuming the existing procedure constructor matched stored JSON.
- Removed unnecessary copied test package references and fixed test compilation errors before validation.
- Self-review tightened production/remote HTTPS enforcement, required migration-aware readiness, kept unknown external acceptance distinct from successful dispatch, and verified cancellation/backpressure does not leave reasoning running blindly.
- PostgreSQL CI exposed a test-host connection setup error: rebuilding pools from a public data-source connection string can lose its password. The HTTP test now combines the fixture's isolated database name with the original test-server credentials without logging them. Added active-batch cancellation regression coverage and a safe request correlation logging scope.

### Verification
- Built the solution and ran Domain, Application, Infrastructure, API, Worker and full solution tests locally with deterministic provider fakes and real loopback HTTP.
- Added isolated PostgreSQL tests for bounded/deferred discovery, concurrent claims, restart after external acceptance, same-key confirmation and authenticated HTTP edited approval/verification/dispatch.
- Database tests require the CI pgvector service or an explicitly configured isolated local server; no live OpenAI, Ollama or ERP calls are claimed.
- Final PostgreSQL CI results are recorded in the Issue #21 PR. Existing Microsoft.OpenApi NU1903 warnings are intentionally not fixed in this feature.

## 2026-09-09

### Delegated to AI
- Helped clarify the assessment scope and the D5/T7 requirements.
- Suggested the initial Clean Architecture solution structure.
- Suggested project references between layers.
- Suggested checking the Microsoft.OpenApi vulnerability warning.

### Written / Verified Manually
- Created the GitHub repository.
- Initialized the local Git repository.
- Created the .NET solution and projects.
- Added the projects to the solution.
- Added project references.
- Ran build and dependency checks manually.

### AI Mistakes / Corrections
- AI suggested adding the latest Microsoft.OpenApi package directly to resolve a transitive vulnerability warning.
- This introduced a compatibility error with Microsoft.AspNetCore.OpenApi 10.0.2 and broke the API build.
- The direct package was removed and the build was restored.
- The vulnerability remains an open dependency issue to resolve with a compatible package upgrade.

### Verification
- Verified the project builds successfully.
- Verified the Clean Architecture project references compile.

## 2026-09-15 — Issue #25 integration and bilingual demo readiness

Codex assisted with completing the pre-existing localization work, creating the original synthetic pump manual and reviewed demonstration procedure, building the separate development-only deterministic demo host, and exercising real PostgreSQL/API/Worker/browser integration. The human approved the bilingual requirement and the existing safety/approval architecture; Codex did not perform or certify physical maintenance checks.

Presentation decisions: standard Accept-Language cultures; resource-backed safe API errors with unchanged codes; persisted UI language only; RTL logical properties and technical identifier isolation; explicit Application narrative culture; original source snippets and exact executable safety scope remain untranslated. ADR-007 records the boundary. The deterministic provider is available only in a separately guarded demo executable, with randomly generated credentials in ignored local artifacts.

Corrections during validation: repaired Arabic literals damaged by a Windows PowerShell encoding boundary; fixed a successful-agent trace incorrectly carrying cannot_proceed; fixed concurrent initial index inserts colliding on secondary unique constraints; named duplicate accessibility landmarks; allowed a new dispatch request after reload only when the server definitively rejected the gate without creating an attempt. Unknown delivery outcomes retain retry protection. Test fixtures use bounded scheduling to avoid overwhelming local Docker storage.

Validation uses actual PostgreSQL/pgvector migrations and ingestion, six bilingual HTTP workflows, browser journeys, safety and approval failures, and durable receiver/Worker reconciliation. Detailed commands and limitations are in DEMO-GUIDE.md. No live OpenAI, Ollama, or external ERP was used. Existing Microsoft.OpenApi NU1903 was deliberately left outside this issue. Final test totals are recorded in the PR after the final validation pass.

## 2026-09-15 - Issue #27 T7 durable reasoning jobs

Codex implemented the human-approved T7-only scope on a separate feature branch:
PostgreSQL queue/leases/events, idempotent 202 submissions, authorized inspection
and cancellation, a separate reasoning hosted service using the existing pipeline,
and transactional fencing of run/order/trace writes. No dispatch authority was
added to reasoning agents. ADR-008 explains coarse safe replay and incomplete
interrupted-attempt accounting rather than claiming exact agent continuation.

Validation includes real PostgreSQL concurrent submission/claim/fencing tests,
HTTP submission without any registered orchestrator, hosted Worker shutdown,
provider cancellation propagation, and a deterministic separate-process kill/restart
harness. The harness runs six bilingual approval/safety/dispatch journeys through
jobs and records safe local proof. It uses an explicit test-only model delay for
crash timing; production providers have no such behavior. No live model quality,
real physical safety verification, ERP integration or monetary costs are claimed.

Corrections: test lease expiry originally violated the queued-row lease constraint;
idempotency test fixtures reused keys across unrelated equipment; the smoke script
used a nonexistent target work-order ID field. Corrected these tests without
weakening constraints. Self-review also made recovered/cancelled results explicitly
report incomplete trace status and preserved Blocked versus technical failure on
recovery. Fixed a PowerShell encoding boundary in the new Arabic documentation.
The old Microsoft.OpenApi NU1903 warning remains outside scope. Final validation
totals and CI are recorded in the PR; the existing Angular diagnosis path remains
request-owned, with the new durable API/harness explicitly documented separately.

Delivery continuation preserved all uncommitted work. Local Docker Desktop could
not initialize its `dockerInference` socket after disk space was recovered. No
pagefile, Docker storage or cache configuration was changed. Final live validation
uses the existing real PostgreSQL/pgvector GitHub Actions service, retaining the
safe proof artifact. Documentation range punctuation damaged by shell encoding was
corrected. The PR must pass CI, including the live-observer/restart smoke, before
delivery is reported complete.

## Issue #29 - FR-1 corpus and ingestion

AI assisted inspection, additive stage contracts, PDF extraction, per-attempt PostgreSQL reporting, synthetic corpus generation, tests and documentation. The corpus is explicitly fictional and not manufacturer guidance. No personal/proprietary data, paid provider call or model download was used. Requested scope excluded evaluation, broad security changes, teaching material and token streaming.

Self-review found that Npgsql redacts connection-string credentials: the dedicated liveness session must use injected host configuration. Fixed before delivery. Reports now support synchronous and asynchronous container disposal. Section-heading detection was moved outside the chunk loop to avoid rescanning a large source per window; PDF identity/locator numbers explicitly use invariant culture. Existing text identities/line endings were preserved; registration assertions were updated to the shared pipeline. Live PostgreSQL tests cover concurrent duplicates, transactional completion, rollback and backend termination. A generated PDF sample was rendered and checked visually. Final totals and CI evidence are reported in the PR. Existing Microsoft.OpenApi NU1903 remains out of scope.

Final failure-category review restricted UnsupportedFormat to extraction input rejection. Unsupported provider operations remain StageFailed at the recorded Embedding stage; a focused regression covers this distinction.

## Issue #31 - FR-3 evaluation

AI assisted the frozen 30-case synthetic golden dataset, isolated adversarial fixtures, evaluation composition root, metric tests and documentation. Existing ingestion/retrieval/SymptomMatcher and deterministic DemoProvider were reused unchanged; expected labels never enter the provider. No paid key, model download, proprietary manual or hidden reasoning is included. Questions and expectations were frozen before baseline measurement; poor results were retained (Hybrid 4/22, grounding proxy 2/12, refusal 1/6). This is a limited deterministic integration baseline, not production LLM quality or injection-security proof.

Self-review corrected raw-byte fingerprint differences between Windows CRLF and Linux LF without changing dataset content, and added an isolated-index contamination check with regression coverage. A full-build DLL lock required temporarily stopping only the local demo API/Worker; it was not a production defect. No production quality fix or before/after improvement is claimed. Full tests, real PostgreSQL repeatability and CI results are recorded in the PR. Existing NU1903 remains out of scope.

Cross-platform CI comparison confirmed identical aggregate metrics and outcomes but exposed existing PDF extractor LF/CRLF differences affecting scalar locators and derived chunk IDs (294 evidence occurrences). Both environments repeat exactly independently. This portability limitation is documented, not hidden by normalizing returned evidence in the evaluator; changing existing ingestion identities is deferred to a versioned ingestion change.

## Issue #33 - OWASP security controls

AI assisted inspection/threat modeling, actor-scoped rate limits, two-role demo configuration, hosted copy-only PII redaction, bounded provider payloads, CSP/CORS/headers, security regressions and pinned scanner CI. Existing authorization, exact evidence, D5 and T7 controls were reused. Microsoft.OpenApi 2.7.5 resolves the known 2.0.0 advisory without suppressing it. Gitleaks 8.30.1 scanned full available history with no findings before implementation; no real secrets were included in prompts/reports. No FR-3 dataset/baseline changes or quality tuning.

Self-review preserved JSON structure/correlation during tool-history redaction, kept original evidence immutable, prevented resource-ID rotation from bypassing actor limits, and documented hosted embedding profile/reindex implications. Limitations include pattern-only PII detection, semantic injection risk, in-memory limiting and production host/identity responsibilities. Final test/scan totals are recorded in the PR.

Validation encountered local DLL locks while tests/demo were active and a Vitest worker-start timeout; no machine configuration was changed. Rebuilding after completion and rerunning Angular resolved these environment failures. A real demo role proof confirms technician 403s and supervisor approval still blocked from dispatch without verified safety. Hosted preprocessing is included in index binding to reject incompatible existing vectors.

First CI exposed the old bursty demo harness treating legitimate 429 as a failed safety 422 assertion. Fixed the harness to honor bounded Retry-After only for explicit pre-execution 429, preserving payload/idempotency and keeping production limits enabled. Added focused Node tests; no security gate or safety assertion was relaxed.

## Issue #35 — Product RAG streaming and surface

AI-assisted implementation continued from the preserved uncommitted first pass after a disk-pressure stop/reboot. Scope: request-owned Ask service/SSE, PostgreSQL conversations, authenticated existing-pipeline ingestion, bilingual Angular pages and capability UX. No frozen FR-3 corpus/baseline change, Domain safety change, provider installation or machine configuration change.

Review focuses on owner/equipment authorization, single-active-turn concurrency and stale finalization, cancellation/timeout disposal, exact structured retrieval provenance, safe history copies, untrusted prompt content, no model HTML, file-label validation, request limits, role-only UX versus server policy, RTL and the distinct T7 observer semantics. Deterministic streaming proof is adapter/test-double evidence, not a claim of live hosted/local model quality. Validation totals and measured proof are recorded in the Issue35 PR/report after execution.

Validation/self-review fixed raw host DI construction for history sanitization, evidence transport bounds without altering source text, permission filtering before conversation pagination, and late Angular history responses replacing a newer selection. Browser fixtures now obtain the real capability identity and preserve memory-only credentials during SPA navigation. HTTP regression coverage exercises actual disconnect cancellation, timeout, PDF/text ingestion, IDOR and rate limits. The existing ingestion crash test now waits for PostgreSQL backend termination instead of racing signal delivery; no production ingestion behavior was changed.

Local Docker initially needed the user's restart repair; existing pgvector storage then ran without machine configuration changes. A parallel demo authorization run hit the enabled 429 limit and passed when run sequentially. Streaming proof recorded seven deltas before completion in both languages, provider disposal after one delta on cancellation, exact persisted citations across API restart, and unchanged T7 process-recovery/D5 safety journeys. Full validation details are included in the PR. Known Node test-environment warnings are separate from the zero-warning .NET build.

## Issue #37 — FR-5 resilience and usage accounting

AI assisted audit, bounded transient read-only retry classification, reuse of grounded Ask for
advisory degradation, physical-provider accounting and owner/equipment-scoped PostgreSQL queries.
No paid model, commercial pricing assertion or frozen FR-3 edit. Existing safety, approval, dispatch
and T7 recovery boundaries were reused. Review found and corrected missing propagation of typed
retrieval failures to fallback, preserved caller cancellation when usage cleanup fails, and retained
exact citations under the bounded workflow SSE transport. Focused tests cover unknown usage, local
cost semantics, streaming cancellation, retry suppression and real HTTP/PostgreSQL fallback.
Validation totals and remaining limitations are reported after execution, not inferred from unit tests.

Local validation: zero-warning .NET build; 666 distinct passing .NET tests including 65 PostgreSQL
integration tests, 28 Angular tests, 30 mocked browser workflows and 3 real-host browser journeys,
3 Node harness tests, TypeScript/production build/format checks. Dependency audit found no findings;
full-history and working-change secret scans passed. Frozen FR-3: 30 cases / 12 adversarial,
unchanged SHA256 ca023d78c65759125bbee3259d9edeb7f201cc3e72e4db30f847285225f52b38.

Live fault proof exercised two attempts, Arabic grounded advisory/refusal, exact citations, no work
order, owner isolation and identical usage call IDs after host restart. Product history/citations
survived restart; T7 exercised actual worker process recovery, disconnect/cancellation and six
bilingual approval/safety/dispatch workflows. A missing DEMO_POSTGRES variable in the first local
T7 command was corrected before rerunning successfully. A 100ms timeout test was too short under
parallel test load and now uses a two-second deadline; production policy did not change. Browser
accessibility found an invalid status role on aside; replaced it with an appropriate div. Final
review exposed missing public trace step IDs; the API now exposes safe IDs and an HTTP integration
test joins every usage step to the trace. Node emitted existing localStorage/color environment
warnings; no compiler/security warnings were suppressed. No live paid/provider quality claim.

## Issue #39 — Deployment packaging (in progress)

AI-assisted packaging audit found existing migrations, FR-1 ingestion, roles,
readiness, persistence and deterministic providers, but no repository-owned
Docker build/Compose path. Added digest-pinned multi-stage builds, production
Angular/Nginx, private PostgreSQL/pgvector, ordered migrations/seed and scoped
random-secret volumes. The existing opt-in Demo harness supplies container
configuration and reuses the normal hosts; production host logic is unchanged.
Corpus and evaluation call existing implementations. Frozen FR-3 data is untouched.

Self-review caught referenced API/Worker appsettings publish collisions: the Demo
publish target now excludes these two default configuration files explicitly,
without suppressing publish conflict checks. POSIX entrypoint line endings are
fixed for Windows clones. Real browser tests now accept an external packaged
origin and credential-file paths without changing their default development path.
Validation and clean-copy evidence will be recorded when completed; image pulls
were initially slow. No live hosted-provider quality claim is made.


Preflight runtime validation used a temporary image of host-published assemblies
while Microsoft SDK image downloads were slow. This is explicitly NOT the final
Docker-only fresh-clone proof. It caught three integration defects: a non-loopback
HTTP Ollama placeholder rejected by the existing transport policy; loss of the
API's safe framework logging default when excluding referenced appsettings; and
Angular critical-CSS loading's inline onload handler being blocked by CSP. Demo
registration now uses an unused validated loopback endpoint, retains safe logging,
and explicitly uses password-only PostgreSQL without probing absent GSS libraries.
Optional real Ollama still requires trusted HTTPS; no transport bypass was added.
Production CSS remains minified but disables critical-style inlining, preserving
strict script CSP. All three real browser journeys passed after this correction.

Preflight corpus ingestion/re-ingestion completed 31 documents/150 PDF pages with
unchanged row counts. Restart retained history/citations/usage identities; a killed
Worker replayed with one work order and an intact dispatch gate. Evaluation reruns
were byte-identical on Linux and summary metrics matched the frozen baseline.
Its older Windows PDF snippets use CRLF, so exact cross-platform evidence IDs and
scalar locators differ; the baseline was not rewritten and this limit is documented.
Local validation: .NET build 0 warnings/errors; 666 tests including all 65 PostgreSQL
tests; 28 Angular tests using a temporary threads runner after the local forks runner
failed to start; 30 mocked plus 3 real-container browser tests; production build,
TypeScript, Node smoke-helper tests, dependency and secret gates passed. Existing
Node localStorage/color warnings remain separate from compiler/security results.

Stopped safely before source-image acceptance/commits: C: fell to approximately
2.79 GiB. The remaining SDK blob still required about 128 MB download plus 534 MB
uncompressed, before restore/publish. Only the verified task-owned build process
tree was stopped; all preflight services were stopped without deleting volumes.
No machine configuration, user files or frozen evaluation data were changed.
The clean acceptance snapshot remains on D: with no acceptance database initialized.
No commit, push or PR was made. Fresh source-only image builds, full clean-copy
proof (including the strengthened recovery assertions), final review and Git/CI
delivery remain pending after safe disk capacity is recovered.

Resumed source-only acceptance found a first-boot readiness race: upstream PostgreSQL
starts a temporary Unix-socket server during initialization, and socket-only
`pg_isready` released migrations before TCP was ready. Both Compose database
health checks now use `-h 127.0.0.1`. A separate new-volume project is used to
retest first boot; the failed attempt and its volumes are preserved. Source SDK
restore/publish succeeded inside Docker without host output. Final acceptance
and delivery results follow when completed.

Final source-built Docker-only acceptance completed (Issue #39, 2026-09-18). All
six images (setup/migrate/seed/api+worker via demo, web, smoke, evaluation) were
built from repository source with .NET restore/publish inside the Linux Docker
build; no host-published assemblies were used. Project `maintenance-proof39-final`
used fresh volumes created after the TCP readiness fix. Services reached Healthy
in the correct dependency order.

Acceptance results: corpus 31 documents/150 PDF pages, idempotent re-ingestion,
bilingual EN/AR Ask streaming with exact citation locators, InsufficientEvidence
refusal, SSE cancellation (state 3 persisted), Technician IDOR/upload 404/403,
Supervisor D5 (6 bilingual scenarios: Approve/Reject/EditAndApprove, stale
conflict 409, spoof 400, unverified safety 422, verified dispatch, trace),
packaging-smoke SPA/proxy/401/usage pass, Worker crash-and-recovery (new attempt,
single work order, dispatch gate intact), full compose down/up with history and
citation identity confirmed. Docker-packaged evaluation: --validate SHA256
ca023d78c65759125bbee3259d9edeb7f201cc3e72e4db30f847285225f52b38 matches frozen
baseline; --repeat byte-identical outcomes. CRLF/LF line-ending difference in
Linux-extracted PDF snippets pre-documented; aggregate metrics unchanged.

Security self-review found no defects: no secrets in images, no DB port exposure,
proxy_ssl_verify on, CORS fail-closed, no privileged containers, all images
digest-pinned, destructive volume guard active, setup-generated credentials never
logged. Final validation: compose config valid, git diff --check clean, gitleaks
55 commits no leaks, NuGet+npm 0 high/critical, frozen FR-3 SHA256 unchanged.
No live paid/provider quality claim; no safety semantics changed by packaging.

## Issue #41 — Documentation, Architecture and Engineering Compliance

### Delegated to AI

AI (Kiro + Codex/Claude) executed the full documentation and engineering
compliance pass for the ITI assessment. Starting from the completed read-only
audit (pre-Issue #41), AI created or updated the following artifacts:

- `docs/SYSTEM-DESIGN.md` — Part A target architecture (18 sections covering API
  gateway, identity, secrets, broker, workers, autoscaling, caching, managed
  PostgreSQL/vector, object storage, observability, CI/CD environments, backup,
  HA, certificate rotation, deployment strategy, and cost model); Part B MVP
  gap table (17 rows with evidence paths, deferred rationale, and mitigation);
  4 design decisions with alternatives considered; explicit not-implemented list.
- `docs/ARCHITECTURE.md` — appended full sequence diagram (202/SSE/3 agents/
  2 safety assessments/approval/dispatch), DFD with trust boundaries (UNTRUSTED
  DATA labels, what the hosted LLM sees and does not see), and ERD (all 20
  entities from the actual SQL migrations 001–007 + 001–002 knowledge).
- `docs/diagrams/` — three canonical Mermaid source files committed as reviewed
  diagram artifacts: `sequence-maintenance-workflow.mmd`, `dfd-trust-boundaries.mmd`,
  `erd-operational-schema.mmd`.
- `prompts/` — versioned prompt library for all three agents (v1.md files) with
  exact prompt text, design intent, output schema, safety invariants, and
  contract test references.
- `tests/IndustrialCopilot.Application.Tests/Reasoning/PromptVersionTests.cs` —
  12 contract tests: 3 runtime-vs-file identity checks, 3 role-identifier checks,
  3 no-authority checks, 3 JSON-output checks. All pass.
- `.kiro/steering/architecture-rules.md` — 10 mandatory invariant rules auto-
  injected by Kiro into every AI session. Active from Issue #41 onwards.
- `docs/AGENTIC-WORKFLOW.md` — documents 5 mechanisms, delegation history across
  20 PRs, human-controlled boundaries, AI mistakes from the log, and risk analysis.
- `LICENSE` (MIT), `CONTRIBUTING.md`, `CODEOWNERS`, `.github/PULL_REQUEST_TEMPLATE.md`,
  `.github/ISSUE_TEMPLATE/bug_report.md`, `.github/ISSUE_TEMPLATE/feature_request.md`.
- `docs/BRD.md` — traceability matrix updated from "Planned" × 16 to
  "Implemented" × 16 with exact evidence paths.
- `README.md` — full rewrite: prerequisites table, environment variables table,
  no-key demo path, how-to-run-tests section, 5-Minute Demo Path (7 numbered
  steps covering ingestion/grounded answer/refusal/multi-agent/approval gate/
  trace-usage/T7 recovery), variant disclosure, key documentation table.
- `teaching/README.md` — teaching pack plan (honestly marked as not yet
  created): session structure, 5 lab tasks, 3 stretch challenges, 6 learning
  outcomes, assessment map, 5 misconceptions.

### Human constraints given

- No changes to frozen FR-3 dataset or baseline.
- No weakening of safety/security/T7 invariants.
- No invented functionality — every documentation claim must be backed by code.
- No file-path dependencies in Docker containers from the prompt library.
- Do not merge the PR.
- Use evidence from actual SQL migration files for the ERD.
- Use exact prompt strings from agent .cs files for the Markdown library.
- Architecture steering rules must reflect actual code invariants, not aspirational rules.

### AI mistakes and self-review corrections

- **Prompt test regex**: Initial implementation extracted the first fenced code
  block in the Markdown file, but the prompt files contain multiple blocks
  (metadata YAML, prompt text, output schema). Corrected to extract the block
  under the `## Prompt Text` heading. Verified all 12 tests pass.

- **ERD relationship for `operations_manuals`**: Initial draft had a direct FK
  from `conversations` to `manuals(id)`, but the actual migration 006 uses a
  composite FK `(document_id, equipment_id)`. Corrected to match the SQL.

- **SYSTEM-DESIGN.md cost model**: First draft showed a specific PostgreSQL
  instance type without qualifying which cloud provider it applied to. Corrected
  to specify "AWS Aurora PostgreSQL" explicitly and add "Included in managed
  cluster costs" for items priced in compute.

- **README `no-key demo path`**: Initial draft implied Ollama could be used with
  plain HTTP on `localhost`. The actual transport policy in `OllamaLlmProvider`
  rejects non-loopback plain HTTP. Corrected to state: "Requires HTTPS gateway
  — plain HTTP is rejected by the provider's transport policy."

- **BRD traceability matrix**: First pass changed all 16 rows from "Planned" to
  "Implemented" without verifying each. Self-review caught that BR-04 (equipment/
  manual revision identification) needed a specific test reference beyond the
  general orchestrator. Added `AgentContractTests.EvidenceMustMatchDocumentAndRevision`
  as the explicit test evidence.

### Verification

- `dotnet build` — 0 warnings/errors.
- `dotnet test --filter PromptVersionTests` — 12 pass.
- `git diff --check` — clean.
- `docker compose config --quiet` — valid.
- `dotnet run --project tools/IndustrialCopilot.Evaluation -- --validate` —
  SHA256 `ca023d78c65759125bbee3259d9edeb7f201cc3e72e4db30f847285225f52b38` unchanged.
- Full `.NET test` suite (666 tests) — confirmed passing after changes.
- Secret scan — clean (no new credential-pattern files introduced).
- No production code changes; no Docker packaging impact; no agent runtime changes.
