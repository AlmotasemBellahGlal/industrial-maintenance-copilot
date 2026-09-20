# Agentic Coding Workflow

This document describes how AI-assisted development was used throughout the
Industrial Maintenance Copilot project, what mechanisms were put in place, and
what was learned — including where AI-assisted coding created risk or produced
incorrect output that required human correction.

---

## 1. Overview

The entire project was built using agentic coding (Kiro IDE with Codex/Claude
sub-agents) under explicit human supervision. The workflow follows an
issue-first, branch-per-feature model. Every PR has an entry in
`docs/AI-USAGE-LOG.md` documenting what was delegated, what constraints were
given, mistakes found, and verification steps performed.

Human control was maintained over:
- Architecture decisions and ADRs.
- Safety policy invariants and approval gate design.
- Frozen evaluation dataset content.
- Merge decisions (no PR was auto-merged).
- Secret management (credentials never generated or committed by AI).

---

## 2. Agentic Mechanisms

The following five mechanisms are implemented and committed in this repository.

### Mechanism 1 — Project Instruction File (`.kiro/steering/architecture-rules.md`)

**What it is:** A repository-level Kiro steering file that is automatically
injected as context into every AI session in this repository. Kiro reads files
in `.kiro/steering/` and treats them as always-active context rules.

**Where:** `.kiro/steering/architecture-rules.md`

**Why it exists:** Without persistent architectural rules, each AI session
starts with no memory of the project's invariants. The steering file encodes
the ten mandatory constraints that no AI change may violate:

1. Clean Architecture dependency direction (Domain ← Application ← Infrastructure).
2. Safety is deterministic code, never LLM output.
3. No dispatch without supervisor approval (server-side check).
4. T7 durability and idempotency rules (lease-based, concurrency-fenced).
5. Grounded citations — no hallucinated provenance.
6. Frozen FR-3 evaluation dataset.
7. No secrets in source control.
8. Agent tool allowlist is fixed (`retrieve_evidence` for SymptomMatcher only).
9. Every code change needs tests and docs.
10. OWASP LLM boundary: retrieved text is always UNTRUSTED DATA.

**How it changes AI behaviour:** On every session start, Kiro injects the full
text of this file as system context. The AI refuses changes that violate these
rules (e.g., moving the safety check inside a prompt, adding dispatch without
approval, expanding the tool allowlist).

**Example impact from Issue #17:** The AI correctly refused to generate a
`dispatch` tool for the WorkOrderGenerator agent because Rule 8 prohibits it.

---

### Mechanism 2 — Versioned Product Prompt Library (`prompts/`)

**What it is:** Canonical Markdown source files for the three agent system
prompts, stored alongside enforcement contract tests.

**Where:**
```
prompts/
  README.md
  symptom-matcher/v1.md
  diagnostic-safety-planner/v1.md
  work-order-generator/v1.md
tests/IndustrialCopilot.Application.Tests/Reasoning/PromptVersionTests.cs
```

**Why it exists:** Agent prompts are safety-critical text. Without versioning:
- Prompt changes are invisible in code review (they look like C# string changes).
- There is no history of why a prompt was worded a particular way.
- It is easy for the runtime string and a documented version to drift silently.

**How it works:** The `prompts/` files are the reviewed, diff-visible source.
The C# agent classes are the runtime source. `PromptVersionTests.cs` reads
the committed Markdown, extracts the prompt from the fenced code block, and
asserts it matches the C# runtime string. A divergence fails the test suite.

**How it changes AI behaviour:** When reviewing a prompt change PR, the diff
shows both the Markdown file and the C# literal side by side. The contract
test gives the AI a clear signal that it must update both files.

**Evidence of utility (Issue #17, #21):** The DiagnosticSafetyPlanner prompt
was refined to explicitly prohibit `mandatory` and `authoritative` labels after
the first version allowed the model to assert safety requirements as policy.
The git blame on `v1.md` shows this change.

---

### Mechanism 3 — Reusable Assessment Skills (`.agents/skills/`)

**What it is:** Kiro skill files that provide reusable design and review
intelligence invokable during AI sessions.

**Where:** `.agents/skills/` contains seven committed skills:
- `ui-ux-pro-max` — UI/UX design intelligence for Angular interface work.
- `design-system` — Token architecture, component specifications.
- `design` — Brand identity and logo generation.
- `brand` — Brand guidelines and messaging.
- `ui-styling` — CSS and styling decisions.
- `banner-design` — Banner and visual asset design.
- `slides` — HTML/CSS presentation generation.

**Why they exist:** The Angular frontend (Issue #23) required accessible,
production-quality UI design without a dedicated designer. The `ui-ux-pro-max`
skill was used to generate the initial design system (`design-system/industrial-
maintenance-copilot/`) and to guide accessible layout, ARIA roles, and responsive
breakpoints.

**How it was used:**
- `ui-ux-pro-max` generated the design-system master document and CSS token
  architecture for the Angular workspace.
- The resulting design choices (safety-state colours, density, RTL support)
  were validated against WCAG 2.1 AA via axe-core in browser tests.

**Honest limitation:** These skills are generic UI/design skills, not
maintenance-domain-specific. They do not understand RAG retrieval or safety
workflows. All domain-specific design decisions (safety gate visual treatment,
approval flow UX) were human-directed.

---

### Mechanism 4 — Automated Quality Gate Scripts (`scripts/security/`)

**What it is:** Committed Python scripts that run repeatable quality and security
gates, used both locally and in CI.

**Where:**
```
scripts/security/scan.py       — full-history secret scan + NuGet/npm dependency audit
scripts/security/demo_roles.py — real PostgreSQL technician/supervisor role proof
```

**Why they exist:** Every PR needs a security gate before merge. A committed,
re-runnable script prevents drift between local developer checks and CI checks,
and documents exactly what "security scan" means for this project.

**How to run:**
```sh
python3 scripts/security/scan.py secrets        # gitleaks full-history scan
python3 scripts/security/scan.py dependencies   # NuGet high/critical + npm audit
python3 scripts/security/demo_roles.py          # role proof against live demo API
```

**How it changes AI behaviour:** The CI `security` job runs both scans on every
PR. The AI knows from the steering file (Rule 7) that secrets must not be
committed, and the scan.py script provides the enforcement mechanism. During
Issue #33 (OWASP compliance), the AI used `scan.py` output to verify that no
credentials appeared in the refactored configuration code.

**Evidence of utility:** During Issue #39 (Docker packaging), gitleaks scanned
55 commits and found 0 leaks, confirming the packaging changes did not
accidentally commit certificate content or generated tokens.

---

### Mechanism 5 — Repeatable Container Smoke Proof (`tools/packaging-smoke.mjs`)

**What it is:** A committed Node.js tool that runs inside the `smoke` Docker
container and executes the complete packaged product proof against real HTTP
boundaries, without any host-side code dependencies.

**Where:** `tools/packaging-smoke.mjs` (and the broader smoke suite:
`demo-smoke.mjs`, `product-smoke.mjs`, `t7-smoke.mjs`, `fr5-smoke.mjs`)

**Why it exists:** The assessment requires proof that the packaged Docker
product works end-to-end. A committed, re-runnable smoke tool:
- Makes the proof reproducible by any evaluator.
- Prevents documentation drift — the documented commands actually run.
- Runs inside the container (no host SDK dependency) — the only mechanism
  that genuinely proves the Docker-only claim.

**How to run:**
```sh
docker compose run --rm --no-deps smoke                # main proof
docker compose run --rm --no-deps smoke --verify-restart
docker compose run --rm --no-deps smoke --submit-recovery
docker compose run --rm --no-deps smoke --verify-recovery
```

**How it changes AI behaviour:** The AI is constrained (by the steering file,
Rule 6) not to modify the frozen evaluation dataset. The smoke tool provides a
separate, mutable proof layer for the packaging claims without touching evaluation.
The AI generates and updates smoke scripts in response to new packaging features.

**Evidence of utility (Issue #39):** The smoke tool discovered a real first-boot
defect — PostgreSQL `pg_isready` on the Unix socket passed before TCP was ready,
allowing migrations to start prematurely. The smoke tool's failure was the
detection signal for this bug. It was fixed in `compose.yaml` and the clean-slate
proof was re-run with fresh volumes.

---

## 3. Summary Table of Mechanisms

| # | Mechanism | Location | Type | Assessment category |
|---|---|---|---|---|
| 1 | Architecture steering rules | `.kiro/steering/architecture-rules.md` | Project instruction file | Project instruction / config |
| 2 | Versioned prompt library | `prompts/`, `PromptVersionTests.cs` | Prompt artifacts + contract tests | Versioned prompt library |
| 3 | Reusable UI/design skills | `.agents/skills/` | Reusable skills | Reusable skills / sub-agents |
| 4 | Security quality gate scripts | `scripts/security/` | Custom repeatable commands | Custom commands |
| 5 | Container smoke proof tool | `tools/packaging-smoke.mjs` et al. | Custom repeatable commands | Custom commands |

Five mechanisms meet the assessment's minimum requirement of five. All five are
genuine, committed, and functional.

**Not claimed:**
- MCP configuration: no MCP server is configured in this repository. Claiming
  one would be fabricated.
- Kiro hooks: no `.kiro/hooks/` configuration exists. Not claimed.

---

## 4. What Was Delegated to AI

The following work was delegated across 20 PRs (Issues #1–#40):

| Issue | Delegated to AI |
|---|---|
| #1 (BRD) | Business requirement drafting from domain description |
| #3 (CI) | Initial GitHub Actions workflow skeleton |
| #5 (Architecture) | Clean Architecture project structure, initial ADRs |
| #7 (Domain model) | Domain entity design (WorkOrder, MaintenanceRun, SafetyRequirement) |
| #9 (Ports) | Application port/interface definitions |
| #11 (LLM providers) | OpenAI + Ollama adapter implementation |
| #13 (RAG) | pgvector hybrid retrieval with RRF |
| #15 (Operations) | PostgreSQL schema + migrations 001–004 |
| #17 (Agents) | Three specialized agents + orchestrator |
| #19 (Safety/Approval) | ISafetyPolicy, approval service, dispatch gate |
| #21 (API/Worker) | ASP.NET Core API, Worker, SSE, HTTP DTOs |
| #23 (Angular) | Angular SPA with design-system skill |
| #25 (E2E demo) | Bilingual demo, browser tests |
| #27 (T7 jobs) | Durable job queue, lease recovery, migration 005 |
| #29 (FR-1 corpus) | 31-document synthetic corpus, ingestion pipeline |
| #31 (FR-3 evaluation) | 30-case golden dataset, evaluation harness |
| #33 (OWASP security) | Security controls, OWASP mapping, prompt injection tests |
| #35 (Product RAG) | Request-owned Ask, conversations, migration 006 |
| #37 (Resilience/usage) | Bounded retries, grounded fallback, llm_usage, migration 007 |
| #39 (Docker packaging) | Multi-stage Dockerfiles, Compose, smoke proof |
| #41 (This issue) | SYSTEM-DESIGN.md, ARCHITECTURE diagrams, prompt library, this document |

---

## 5. What Remained Human-Controlled

- Merge decisions for every PR.
- Architecture decisions before implementation (e.g., choosing PostgreSQL over
  RabbitMQ for the T7 queue — ADR-008).
- Safety policy procedure definition (`demo/reviewed-procedures.json`).
- Final acceptance of evaluation golden dataset case content.
- Security review of authentication scheme design.
- Decision to use the deterministic `DemoProvider` rather than a real LLM for CI.
- Decision to preserve honest poor FR-3 scores rather than tuning the evaluator.
- Decision NOT to implement features explicitly out of scope (ERP integration,
  sensor telemetry, autonomous dispatch).

---

## 6. AI Mistakes and How They Were Caught

The following are representative examples from `docs/AI-USAGE-LOG.md`.
The full log covers all 21 issues.

### Issue #21 (API/Worker)

**Mistake:** The HTTP test host's isolated database name was constructed using
only the connection string's database name, losing the password when combined
with the test server's credentials.

**How caught:** PostgreSQL integration tests failed with authentication errors
on CI after the test database isolation was added.

**Fix:** The HTTP test now combines the fixture's isolated database name with
the original test-server credentials. Added a regression test for the connection
behaviour.

### Issue #23 (Angular frontend)

**Mistake:** Native form submission accidentally reloaded the session, breaking
credential state under Angular's change detection.

**How caught:** Real browser tests detected the page reload after form submission.

**Fix:** Prevented default form submission and added a non-reactive confirmation
label fix.

**Mistake:** Low-contrast transitional disabled-button colours.

**How caught:** Axe-core accessibility scan in CI found contrast violations.

**Fix:** Retained visible keyboard focus rings; removed the low-contrast colours.

### Issue #37 (Resilience/usage)

**Mistake:** Missing propagation of typed retrieval failures to the grounded
fallback path. A `DependencyFailureException` was not being re-thrown correctly
when the fallback itself failed to retrieve evidence.

**How caught:** Focused unit tests on the fallback path revealed the exception
was swallowed.

**Fix:** Corrected the exception propagation; added regression test.

### Issue #39 (Docker packaging)

**Mistake:** First container proof used host-published assemblies (not genuine
source builds). This invalidated the Docker-only claim.

**How caught:** The CI acceptance test requirement specified no host SDK
dependency. The AI itself identified this after inspecting the build context.

**Fix:** All images rebuilt from repository source inside Linux Docker with
`.NET restore` and `dotnet publish` inside the build stage.

**Mistake:** First batch file for the Worker recovery proof ran `--submit-recovery`
before stopping the normal Worker, allowing the normal Worker to complete the job
before the delayed test Worker could claim it.

**How caught:** `--verify-recovery` asserted `attempts.length == 2` but got `1`.

**Fix:** Corrected the sequence to: stop worker → start delayed worker → submit
recovery → kill delayed worker → restart normal worker → verify recovery.

### Issue #41 (This issue)

**Mistake (self-review, corrected before commit):** The initial `PromptVersionTests`
used a regex that extracted only the first fenced code block in a Markdown file,
but the prompt files contain metadata code blocks as well as the prompt block.

**How caught:** Self-review of the test output against the actual Markdown structure.

**Fix:** Changed extraction to target the block immediately following the
`## Prompt Text` heading, ensuring the correct block is extracted even if
there are other fenced blocks in the file.

*(Note: This fix was applied during writing the test. The 12 tests currently pass.)*

---

## 7. Where Agentic Coding Created Risk

### Risk 1: Documentation claiming unimplemented features

Several early draft documents used future tense for features that were not yet
built. After the read-only audit (pre-Issue #41), all documentation was audited
and forward-looking claims were either backed by implementation evidence or
explicitly marked as deferred in the new `SYSTEM-DESIGN.md` gap table.

### Risk 2: Safety instruction drift in prompts

Without the versioned prompt library (added in Issue #41), prompt changes were
invisible to reviewers — they looked like C# string changes with no semantic
context. A reviewer would not know whether the change strengthened or weakened
a safety invariant. The `prompts/` directory and contract tests resolve this.

### Risk 3: Test isolation failures

Several PostgreSQL integration tests initially shared the same database or used
a fixed database name, creating interference between parallel test runs.

**Resolution:** Each test creates an isolated database with a unique name.
The corpus tool requires `maintenance_corpus`; the evaluation harness requires
`maintenance_evaluation`; the demo requires `maintenance_demo`. None share a
database.

### Risk 4: Scope creep in generated code

The AI occasionally generated utility code or helper abstractions beyond what
the minimal task required. In Issue #35, the AI added a generic `IPagedQuery`
abstraction that was not needed for the assessment scope.

**Resolution:** The human explicitly rejected it. The commit message for that
PR notes: "rejected IPagedQuery abstraction — not needed for MVP scope."

### Risk 5: Over-claiming evaluation quality

The FR-3 evaluation baseline shows poor retrieval quality (0% keyword, 18%
hybrid) because the assessment uses deterministic hash embeddings. Earlier draft
documentation implied the evaluation was a production quality claim.

**Resolution:** `EVALUATION.md` explicitly documents this limitation. The scores
are honest. The `--repeat` command proves byte-identical reproducibility, which
is what the assessment requires.

---

## 8. Verification Process

Every AI-assisted issue was verified using the following process before merge:

1. `dotnet build` — zero warnings/errors.
2. `dotnet test` — all existing tests pass; new tests added.
3. `python3 scripts/security/scan.py secrets` — no findings.
4. `python3 scripts/security/scan.py dependencies` — no high/critical.
5. `git diff --check` — no whitespace errors.
6. For packaging changes: full Docker source build + container smoke proof.
7. For evaluation changes: `--validate` SHA256 check + `--repeat` byte-identity.
8. Self-review of the full diff before requesting merge.

CI runs the same gates independently on every PR via `.github/workflows/ci.yml`.
