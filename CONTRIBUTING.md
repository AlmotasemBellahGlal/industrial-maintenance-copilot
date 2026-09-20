# Contributing to Industrial Maintenance Copilot

Thank you for contributing. This document describes the engineering process,
quality gates and conventions for this repository.

This is an ITI Technical Instructor Assessment project (D5 + T7 variant).
All contributions must preserve the safety, security and durability invariants
documented in `.kiro/steering/architecture-rules.md`.

---

## Table of Contents

1. [Issue-first workflow](#1-issue-first-workflow)
2. [Branching conventions](#2-branching-conventions)
3. [Commit conventions](#3-commit-conventions)
4. [Pull request requirements](#4-pull-request-requirements)
5. [Tests](#5-tests)
6. [Security](#6-security)
7. [Documentation](#7-documentation)
8. [AI-assisted contributions](#8-ai-assisted-contributions)
9. [Frozen evaluation data](#9-frozen-evaluation-data)
10. [No secrets](#10-no-secrets)

---

## 1. Issue-first workflow

Every change starts with a GitHub Issue.

- Create an issue before opening a branch.
- Describe: what you want to change, why, and the acceptance criteria.
- Label the issue appropriately (`bug`, `docs`, `feature`, `security`, etc.).
- Your PR must link the issue using `Closes #<number>` in the PR description.

---

## 2. Branching conventions

Branch off `main`. Use the following naming scheme:

```
feature/<issue>-short-description
fix/<issue>-short-description
docs/<issue>-short-description
security/<issue>-short-description
refactor/<issue>-short-description
```

Examples:
```
feature/41-docs-architecture-compliance
fix/42-worker-lease-timeout
docs/43-update-deployment-guide
```

- Never commit directly to `main`.
- Delete your branch after the PR is merged.
- Keep branches short-lived — open the PR as soon as you have meaningful progress.

---

## 3. Commit conventions

Use the conventional-commit format:

```
<type>(<scope>): <subject>

<body — optional>

<footer — optional>
```

Types: `feat`, `fix`, `docs`, `test`, `refactor`, `security`, `chore`, `ci`.

Examples:
```
feat(agents): add symptom confidence score to SymptomMatchResult
fix(worker): correct lease expiry calculation under clock skew
docs(architecture): add ERD for operational schema
test(application): add prompt version contract tests
```

- Subject: imperative mood, ≤72 characters, no trailing period.
- Body: explain what and why, not how. Reference issue numbers.
- One logical change per commit.
- Do not mix formatting/whitespace changes with logic changes.

---

## 4. Pull request requirements

Before requesting review, your PR must:

- [ ] Reference the issue: `Closes #<N>` in the PR body.
- [ ] Have a clear description: what changed, why, how it was tested.
- [ ] Pass all CI checks (packaging, security, frontend, validate jobs).
- [ ] Include new or updated tests for every logic change.
- [ ] Include documentation updates where the change affects public behaviour.
- [ ] Have been self-reviewed: read your own diff before requesting review.
- [ ] Not contain secrets, credentials, or `.env` files with real values.
- [ ] Not modify `evaluation/golden-v1.json` or `evaluation/golden-v1.sha256`
      without a separate documented evaluation review.

Use the PR template (`.github/PULL_REQUEST_TEMPLATE.md`) — it will be
pre-populated when you open a PR on GitHub.

---

## 5. Tests

### Running tests

```sh
# All .NET tests (deterministic, no external dependencies)
dotnet test

# .NET tests with real PostgreSQL integration tests
$env:RAG_TEST_POSTGRES = "Host=127.0.0.1;Port=5432;Database=postgres;Username=postgres;Password=<password>"
dotnet test

# Angular unit tests
cd src/IndustrialCopilot.Web
npm test

# Angular type check and production build
npm run typecheck
npm run build

# E2E browser tests (requires running demo API)
npm run test:e2e

# Node smoke tests
node --test tools/smoke-http.test.mjs

# Security scans
python3 scripts/security/scan.py secrets
python3 scripts/security/scan.py dependencies
```

### Test requirements

- Every new Application use case → unit test in `IndustrialCopilot.Application.Tests`.
- Every new Infrastructure adapter → integration test with real PostgreSQL.
- Every new API endpoint → HTTP integration test in `IndustrialCopilot.IntegrationTests`.
- Every new domain rule → domain test in `IndustrialCopilot.Domain.Tests`.
- Prompt change → update `prompts/<agent>/v<N>.md` AND the C# literal.
  The `PromptVersionTests` will fail if they diverge.
- Do not disable or skip existing tests to make a PR pass.

### Test isolation

- PostgreSQL integration tests use isolated databases. Never test against a
  database that holds real or production data.
- The evaluation harness uses a dedicated `maintenance_evaluation` database.
  Do not share it with the demo or corpus databases.
- Deterministic provider fakes (`DemoProvider`) are the default for unit tests.
  Real provider tests are opt-in and CI-only.

---

## 6. Security

- Run `python3 scripts/security/scan.py secrets` before pushing. A leak in
  history is much harder to clean than a pre-push check.
- Run `python3 scripts/security/scan.py dependencies` to check for high/critical
  NuGet and npm vulnerabilities.
- The CI `security` job runs both scans on every PR with full Git history
  (`fetch-depth: 0`).
- Never commit `.env` files, credentials, private keys, or API tokens.
- If you suspect a secret was committed, rotate it immediately before the repo
  is made public and contact the repository owner.
- Review OWASP LLM Top 10:2025 invariants in `.kiro/steering/architecture-rules.md`
  before changing anything in `AgentRuntime`, agent prompt text, or retrieval.

---

## 7. Documentation

- Architecture changes → create or update an ADR in `docs/adr/`.
  Use `ADR-NNN-short-description.md`. Mark superseded ADRs explicitly.
- Agent prompt changes → update `prompts/<agent>/v<N>.md`.
  Create a new version file (`v2.md`) rather than overwriting `v1.md`.
- API changes → update `docs/DEPLOYMENT.md` if they affect the evaluator path.
- New features → update `README.md` if they affect the quick-start or demo path.
- All claims in documentation must be backed by implementation evidence.
  Do not document planned features as if they exist.

---

## 8. AI-assisted contributions

This project uses AI-assisted development (Kiro IDE with Codex/Claude).
The following rules apply to AI-assisted sessions:

- The `.kiro/steering/architecture-rules.md` file is injected into every session.
  These rules constrain what changes the AI will make.
- Every AI-assisted PR must have an `AI-USAGE-LOG.md` entry covering:
  - What was delegated to AI.
  - What human constraints were given.
  - AI mistakes found and corrected.
  - Self-review findings.
  - Verification performed.
- The human contributor is responsible for the correctness of AI-generated code.
  "The AI wrote it" is not an excuse for a broken or insecure change.
- AI must not generate, suggest, or commit secrets or credentials.
- AI must not weaken safety or approval invariants.
- AI must not modify frozen evaluation data.

---

## 9. Frozen evaluation data

`evaluation/golden-v1.json`, `evaluation/golden-v1.sha256`, and
`evaluation/baselines/` are frozen for the duration of the ITI assessment.

- Do not edit these files to improve evaluation scores.
- Do not add cases that align with current model behaviour.
- The evaluator validates the SHA256 on every run. Changes will fail CI.
- If evaluation methodology must change for a legitimate reason, open an issue,
  discuss it, and document the decision in a new baseline directory.

---

## 10. No secrets

The following are never permitted in any commit, including WIP commits and
squashed history:

- API keys (OpenAI, etc.).
- Database passwords.
- Bearer tokens or credential files.
- Private TLS keys (`.pfx`, `.pem`, `.key`).
- `.env` files with real values.
- The `.env.example` file is safe and intentional — it contains only defaults.

The `.gitignore` covers common patterns. The CI `security` job uses gitleaks
with `--log-opts=--all` to scan complete history on every PR.

If you are unsure whether something is sensitive, do not commit it.
