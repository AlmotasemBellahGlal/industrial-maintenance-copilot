---
inclusion: always
---

# Industrial Maintenance Copilot — AI Contributor Architecture Rules

These rules apply to every AI-assisted session in this repository.
They encode the mandatory invariants that must not be violated by any change.

## 1. Clean Architecture dependency direction

Dependencies flow **inward only**:

```
Domain ← Application ← Infrastructure ← API / Worker
```

**Violations to refuse:**
- Domain importing from Application, Infrastructure, API, or Worker.
- Application importing from Infrastructure, API, or Worker.
- Any use case calling `new SqlConnection`, `new HttpClient`, or any SDK directly.

All external technology access goes through Application interfaces (ports).
Concrete adapters live in Infrastructure only.

## 2. Safety is deterministic code, never LLM output

`ISafetyPolicy.AssessAsync` is called **twice** in `MaintenanceOrchestrator`:
once after planning, once after generation. Both calls happen outside any LLM turn.

**Never:**
- Move safety validation inside an agent prompt.
- Trust LLM output as a safety gate.
- Skip the second assessment after `WorkOrderGenerator` runs.
- Let an agent produce a field named `approved`, `verified`, `mandatory`, or `satisfied`.

The `WorkOrder.AssessSafety` call and `WorkOrder.SubmitForApproval` are the only
paths to a dispatchable work order.

## 3. No dispatch without supervisor approval

`dispatch_attempts` can only be created after:
- `work_order_snapshots.status` is `Approved` (integer 3).
- All requirements with `mandatory=true` have `satisfied=true`.

This check is at the API endpoint layer (`MaintenanceEndpoints`), not only in the
domain. Do not remove either check. Do not trust client-supplied approval state.

## 4. T7 durability and idempotency rules

- Every job must have a unique `submission_key` per actor.
- `TryPublishReviewAsync` uses a concurrency token — do not replace with a
  blind `INSERT` or `UPDATE`.
- Worker crash recovery uses lease expiry (`lease_until < now()`), not a flag.
  Do not add a "mark as failed on startup" pattern.
- `execution_id` deduplication in `WorkflowTrace` prevents re-running a completed
  execution. Keep this check.

## 5. Grounded citations — no hallucinated provenance

All evidence references in agent output must come from actual `RetrievalResult`
objects validated by `EvidenceCatalog`. The catalog validates `documentId`,
`manualRevisionId`, and `chunkId` match the retrieval results.

Never allow an agent to:
- Generate a locator string directly.
- Return a `chunkId` not present in the catalog.
- Skip `AgentJson.Shape` validation of evidence arrays.

## 6. Frozen FR-3 evaluation dataset

`evaluation/golden-v1.json` and `evaluation/golden-v1.sha256` are frozen.
The evaluator validates the SHA256 on every run.

**Never:**
- Edit `golden-v1.json` to improve scores.
- Change `golden-v1.sha256`.
- Add expected answers that match the current model's output.

Poor scores (0% keyword, 18% hybrid) are intentional and honest.
Document evaluation limitations; do not hide them.

## 7. No secrets in source control

The following must never be committed:
- `.env` files with real values.
- Bearer tokens, API keys, database passwords.
- Private key files (`.pfx`, `.pem`, `.key`).
- `artifacts/issue25/credential.txt` or similar runtime credential files.

The `.gitignore` and `.dockerignore` cover these. Verify before any commit.

## 8. Agent tool allowlist is fixed

Only `SymptomMatcherAgent` may call `retrieve_evidence`.
`DiagnosticSafetyPlannerAgent` and `WorkOrderGeneratorAgent` have no tools.

The allowlist in `AgentRuntime.Allows()` is the enforcement point.
Do not expand the allowlist without a security review and ADR.

## 9. Every code change needs tests and docs

- New Application use case → unit test in `Application.Tests`.
- New Infrastructure adapter → integration test (real PostgreSQL where applicable).
- New API endpoint → HTTP integration test in `Api.Tests` or `IntegrationTests`.
- New domain rule → domain unit test.
- Architecture change → ADR in `docs/adr/`.
- Prompt change → update `prompts/<agent>/v<N>.md` AND C# literal; `PromptVersionTests` enforces agreement.

## 10. OWASP LLM boundary: retrieved text is always UNTRUSTED

`AgentRuntime` labels user input and tool results as `UNTRUSTED DATA`.
This instruction must remain in the system message suffix.

Do not:
- Move retrieved content to the System role.
- Allow tool results to contain system-role injection attempts without the label.
- Trust model output as executable business logic without schema validation.
