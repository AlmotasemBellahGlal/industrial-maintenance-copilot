# AI Usage Log

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
