# Five Common Misconceptions
## Agentic RAG — One-Page Handout for Learners

**Session:** Agentic RAG in Practice: Grounding, Safety, and Human Approval  
**Repository:** Industrial Maintenance Copilot  
*Cut or fold here — give to students at the start of the session.*

---

## Misconception 1
### "The LLM decides whether a work order can be dispatched."

**The reality:** The LLM *proposes*. Application code *decides*.

`WorkOrder.AssessSafety()` and the dispatch endpoint's status check are both in
deterministic C# code. No LLM prompt — however carefully worded — can change the
outcome of `if (workOrder.Status != WorkOrderStatus.Approved) return 422`.

A work order that the LLM "approved" in its output text is still in `PendingApproval`
state in the database until a human supervisor explicitly calls the approve endpoint.

**Why it matters:** Safety systems that depend on prompts can be bypassed by prompt
injection or model imprecision. Safety systems that depend on code cannot.

**See it in the code:** `MaintenanceEndpoints.cs` dispatch handler; `WorkOrder.cs` state machine.

---

## Misconception 2
### "RAG retrieval is semantically accurate — if the answer is in the corpus, it will be found."

**The reality:** Retrieval quality depends heavily on the embedding model and query form.

The FR-3 evaluation shows **0% keyword hit** and **41% dense hit** with deterministic
256-dim hash embeddings. Even with these embeddings, 13 of 18 normal retrieval cases miss.

RAG is only as good as the embedding space and the query-document similarity. "Semantically
related" in human terms does not automatically mean "high cosine similarity" in the
embedding space.

**Why it matters:** Deploying RAG without measuring retrieval quality creates false
confidence. A system that says "cited source: pump manual" may have retrieved the wrong
page, wrong revision, or irrelevant content.

**See it in the code:** `docs/EVALUATION.md` metrics table; `EvaluationRunner.cs`.

---

## Misconception 3
### "Disconnecting the SSE stream cancels the running job."

**The reality:** The durable job is owned by the Worker process, not the HTTP connection.

Closing the browser, losing network connectivity, or calling `reader.cancel()` on the
SSE stream terminates only the *observation* of progress — not the job itself. The Worker
continues executing and will complete, fail, or time out independently.

To cancel a running job, an explicit `POST /api/jobs/{id}/cancel` is required, which sets
`cancellation_requested = true` in the database. The Worker checks this flag at safe
boundaries and sets the job to `Cancelled`.

**Why it matters:** This is one of the core reasons the T7 durable job pattern exists.
Long-running AI workflows must survive network interruptions.

**See it in the code:** `packaging-smoke.mjs` `--submit-recovery` section; `MaintenanceOrchestrator.cs` cancellation handling.

---

## Misconception 4
### "You can make AI behaviour safe by telling the model not to do harmful things."

**The reality:** Prompt instructions are defence-in-depth, not the primary safety gate.

The system's safety invariants are enforced by:
1. **Code:** `ISafetyPolicy.AssessAsync()` called twice; dispatch gate checks
2. **Schema validation:** `AgentJson.Shape()` rejects any output field not in the contract
3. **Tool allowlist:** `AgentRuntime.Allows()` — one line of code, not a prompt
4. **State machine:** `WorkOrder` status transitions require explicit method calls

A prompt that says "never approve without human sign-off" is useful context, but it is
not auditable, not enforceable, and can be ignored by the model or overridden by
an adversarial input. The code gates cannot be argued with.

**Why it matters:** OWASP LLM Top 10 (LLM08: Excessive Agency) specifically calls out
reliance on AI self-restraint without technical controls.

**See it in the code:** `AgentRuntime.cs` Allows(); `PostgresKnowledgeStore.cs` SQL parameterisation.

---

## Misconception 5
### "A poor evaluation score means the retrieval system is broken."

**The reality:** Poor scores on a *deterministic* evaluation with *hash-bucket embeddings*
are expected and documented.

The FR-3 evaluation uses synthetic embeddings (256-dim, SHA256 hash buckets) with no
semantic understanding. Keyword retrieval hits 0% because the evaluation sends full
natural-language questions, not keyword terms. Dense retrieval hits 41% — limited by
the hash-bucket collision rate.

What the evaluation *is* measuring:
- That the dataset is frozen and tamper-evident (SHA256 verified)
- That results are deterministically reproducible (`--repeat`)
- That adversarial injection cases do not leak the `EVAL_OVERRIDE_ACCEPTED` marker
- That revision-scoped retrieval finds the correct revision

**Why it matters:** Evaluation harness quality matters as much as evaluation scores.
A reproducible, honest 41% is more valuable than an unreproducible, optimistic 90%.

**See it in the code:** `docs/EVALUATION.md` limitations section; `EvaluationRunner.cs` `--repeat` logic.

---

*Full session slides, lab guide, and answer key: `teaching/` directory in the repository.*
