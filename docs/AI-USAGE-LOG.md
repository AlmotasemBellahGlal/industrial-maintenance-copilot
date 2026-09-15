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
