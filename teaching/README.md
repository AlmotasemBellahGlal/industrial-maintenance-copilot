# Teaching Pack — Industrial Maintenance Copilot

**Status: PLANNED — not yet created.**

This directory will contain the full teaching pack when completed.
The content listed below defines the planned deliverables exactly as required
by the ITI Technical Instructor Assessment.

The teaching pack has not been created yet. This file documents the plan
and recommended topic. No content is present beyond this README.

---

## Planned Deliverables

Per the ITI assessment requirements, the teaching pack must include:

| Deliverable | Description |
|---|---|
| **Slide deck** | 15–25 slides for a 90-minute post-graduate session |
| **Hands-on lab** | Practical exercises against this repository |
| **Answer key** | Complete answers to all lab tasks and stretch challenges |
| **Learning outcomes** | Explicitly stated, measurable outcomes |
| **Assessment map** | Mapping from outcomes to assessment evidence |
| **Misconceptions page** | One-page five common trainee misconceptions |

---

## Recommended Teaching Topic

**"Agentic RAG: Safety-Constrained Multi-Agent Systems"**

This topic is recommended because it represents the most technically distinctive
aspect of this repository and aligns directly with emerging post-graduate
curriculum needs in AI systems engineering.

### Why this topic

1. **Novel and non-trivial.** The combination of RAG + multi-agent orchestration
   + deterministic safety gates + durable async jobs is not covered by standard
   LLM tutorials.

2. **Directly grounded in code.** Every concept (agent pipeline, trust boundary,
   approval gate, vector retrieval, T7 durability) has a concrete, runnable
   implementation in this repository that students can inspect and modify.

3. **Rich misconception landscape.** Students commonly conflate LLM output with
   authoritative decisions, assume RAG retrieval is semantically perfect, and
   believe streaming SSE is the same as durable job state. All three are
   demonstrably wrong with this codebase.

4. **Assessment-tractable.** Hands-on lab tasks (run evaluation, inspect trace,
   attempt to bypass approval gate, modify a prompt and watch the contract test
   fail) have clear, observable correct outcomes.

---

## Planned Session Structure (90 minutes)

### Part 1 — Why Agentic RAG Is Different (20 min)

- The maintenance decision problem: why LLM knowledge alone is insufficient.
- What RAG adds: grounded retrieval + citation traceability.
- What agents add: tool use, multi-step reasoning, structured output contracts.
- The trust problem: why retrieved document text is UNTRUSTED DATA.
- Demonstration: run a query with `DEMO_PROVIDER=Demo`, inspect the citation locator.

### Part 2 — Sequential Pipeline Architecture (25 min)

- Clean Architecture: why the orchestrator does not call `HttpClient` directly.
- The three agents: SymptomMatcher (tool user), Planner (no tools), Generator (no tools).
- Why only SymptomMatcher has a tool (`retrieve_evidence`).
- Safety as deterministic code: the two `ISafetyPolicy.AssessAsync` calls.
- Human approval gate: why `WorkOrder.SubmitForApproval` requires code, not a prompt.
- Live diagram walkthrough: `docs/ARCHITECTURE.md` sequence diagram.

### Part 3 — T7: Durable Async Jobs (20 min)

- Why long-running AI workflows cannot live in an HTTP request.
- PostgreSQL as a job queue: lease-based exclusion, idempotency key, recovery.
- Worker crash and recovery proof (live demo or pre-recorded).
- SSE progress vs. durable job state: these are different things.
- The idempotency invariant: why `TryPublishReviewAsync` uses a concurrency token.

### Part 4 — Evaluation and Honest Assessment (15 min)

- The FR-3 golden dataset: 30 cases, 12 adversarial.
- Why the baseline is poor (0% keyword, 18% hybrid) and why that is honest.
- What the deterministic provider is actually testing.
- CRLF/LF cross-platform limitation: documented and not hidden.
- Run `evaluation --validate` live.

### Part 5 — Hands-on Lab Introduction (10 min)

- Lab setup: `docker compose up -d --wait`.
- Lab task overview (see Lab section below).
- Q&A.

---

## Planned Hands-on Lab

**Prerequisites:** Docker Desktop installed. Approximately 10 minutes for first build.

### Lab Task 1 — Explore the citation pipeline

1. Run `docker compose run --rm --no-deps corpus`.
2. Open the UI, connect as Supervisor, and ask "pump vibration".
3. Inspect the citation: what is the `locator`? What is the `snippet`?
4. Ask "quasar astrophysics". What happens? Why?

**Expected outcome:** Student correctly identifies the `text:lines N-M` locator
format, the evidence catalog validation, and the `InsufficientEvidence` refusal.

### Lab Task 2 — Trace the agent pipeline

1. Start a diagnosis for `pump vibration` on equipment
   `11111111-1111-1111-1111-111111111111`.
2. Watch the live progress events in the UI.
3. After completion, open the Trace view.
4. Count the agent steps. Which agent used the `retrieve_evidence` tool?
5. What is the correlation ID and how does it link to usage records?

**Expected outcome:** Student identifies 3 agent steps, 1 tool call under
SymptomMatcher, and can link the trace to a usage record via `correlationId`.

### Lab Task 3 — Attempt to bypass the approval gate

1. Complete a diagnosis to get a work order in `PendingApproval` state.
2. Using `curl` or the browser dev tools, attempt to POST to
   `/api/work-orders/{id}/dispatch` directly.
3. What HTTP status code do you receive? Why?
4. What additional steps are required before dispatch succeeds?

**Expected outcome:** Student receives 422, understands the server-side check
for `status=Approved AND all mandatory requirements satisfied=true`, and can
explain why this cannot be bypassed via the client.

### Lab Task 4 — Modify a prompt and observe the contract test

1. Open `prompts/symptom-matcher/v1.md`.
2. Change one word in the prompt text (e.g., "Retrieve" → "Search").
3. Run `dotnet test --filter PromptVersionTests`.
4. What fails? Why?
5. Now update `src/IndustrialCopilot.Application/Reasoning/SymptomMatcherAgent.cs`
   to match your change. Run the test again.

**Expected outcome:** Student understands the prompt versioning contract, the
`PromptVersionTests.cs` enforcement mechanism, and the role of the `prompts/`
directory as a reviewed artifact.

### Lab Task 5 — Run the evaluation and interpret results

1. Run `dotnet run --project tools/IndustrialCopilot.Evaluation -- --validate`.
2. Run the full evaluation (requires PostgreSQL; see `docs/EVALUATION.md`).
3. Find a case in the results where the system answered when a refusal was expected.
4. Why did the model answer? What would change this behaviour with a real provider?

**Expected outcome:** Student interprets retrieval metrics honestly, understands
the `DemoProvider` limitation, and can explain the difference between evaluation
quality and production semantic quality.

---

## Stretch Challenges

### Stretch 1 — Add a new agent

Design (but do not necessarily implement) a fourth agent: an **Impact Assessor**
that estimates the operational risk of deferring the proposed maintenance.

Questions to answer:
- Where in the pipeline would it run?
- What tools would it need?
- What new invariants would the `AgentRuntime.Allows()` method need?
- Would its output require a new deterministic safety check?
- What new application port would it depend on?

### Stretch 2 — Scale the Worker

The Worker is a single process. The lease-based architecture already supports
multiple Workers. Configure the Docker Compose file to run two Worker replicas
and observe that only one processes each job (lease exclusion).

What could go wrong if the lease timeout is set too short?
What is the correct relationship between lease duration and `DEMO_MODEL_DELAY_MS`?

### Stretch 3 — Implement a production embedding provider

The demo uses 32-dimensional deterministic hash embeddings. Replace it with
OpenAI `text-embedding-3-small` (1,536 dimensions) using a fresh Compose project
with a new `EMBEDDING_PROFILE`.

Observe how the retrieval metrics change.
Document why you cannot mix profiles in the same corpus index.

---

## Learning Outcomes

By the end of the session, students will be able to:

1. Explain the difference between a RAG pipeline and an agentic RAG pipeline,
   and identify when each is appropriate.
2. Describe the Clean Architecture dependency rules and explain why they prevent
   agent code from calling databases or LLM SDKs directly.
3. Trace the complete data flow of a maintenance diagnosis, from HTTP request
   through three agents, safety validation, and supervisor approval, to dispatch.
4. Identify the trust boundary for retrieved document content and explain the
   OWASP LLM Top 10 prompt injection defence mechanism.
5. Explain why the T7 durable job pattern is necessary for long-running AI
   workflows and describe the lease-based recovery mechanism.
6. Run the FR-3 evaluation, interpret the metrics honestly, and explain why
   poor scores on a deterministic provider are expected and not hidden.

---

## Assessment Map

| Learning Outcome | Lab Task | Stretch Challenge | Assessment Evidence |
|---|---|---|---|
| 1. RAG vs Agentic RAG | Task 1 | — | Citation locator + refusal observation |
| 2. Clean Architecture | Task 4 | Stretch 1 | Prompt contract test behaviour |
| 3. End-to-end trace | Task 2 | — | Trace view + usage correlation |
| 4. Trust boundary | Task 3 | — | 422 response explanation |
| 5. T7 durability | Demo (Part 3) | Stretch 2 | Recovery attempt count |
| 6. Honest evaluation | Task 5 | Stretch 3 | Metric table interpretation |

---

## Five Common Trainee Misconceptions

**1. "The LLM decides whether a work order can be dispatched."**

The LLM proposes. Application code decides. `WorkOrder.AssessSafety` and the
server-side dispatch check are not part of any prompt. The model cannot approve
its own output.

**2. "RAG retrieval is semantically perfect."**

The FR-3 baseline shows 0% keyword hit and 18% hybrid hit on the assessment
corpus with deterministic embeddings. Real semantic retrieval is better but not
perfect. Citation traceability is the honesty mechanism — every cited fact must
resolve to an actual retrieved chunk.

**3. "Disconnecting the SSE stream cancels the durable job."**

`product-smoke.mjs` explicitly demonstrates this: SSE disconnect leaves the
job in `Running` state. The durable job is owned by the Worker, not the HTTP
connection. Explicit `/cancel` is required.

**4. "You can test AI safety by asking the model not to do something harmful."**

The system's safety invariants are in code (`ISafetyPolicy`, dispatch gate),
not in prompts. Prompt wording is defence-in-depth. The deterministic safety
check cannot be bypassed by a cleverly worded prompt.

**5. "A poor evaluation score means the system is broken."**

The evaluation measures retrieval quality on the assessment corpus with
deterministic hash embeddings. Poor scores are expected and documented. The
evaluation harness itself (`--validate`, `--repeat`, byte-identical reproducibility)
is what the assessment measures, not the scores.

---

## Human Action Required

The full teaching pack (slide deck, complete answer key) must be created by a
human instructor before submission. This README defines the plan; the actual
deliverables do not yet exist.

**Estimated effort:** 8–12 hours for a first version of the slide deck and
answer key.

**Recommended approach:**
1. Run all five lab tasks yourself to verify they work as described.
2. Create the slide deck using the session structure above.
3. Complete the answer key for all lab tasks and stretch challenges.
4. Review the five misconceptions page for your specific cohort.
5. Record the 10-minute teaching sample video (separate assessment requirement).
