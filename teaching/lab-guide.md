# Hands-on Lab Guide
## Agentic RAG in Practice: Grounding, Safety, and Human Approval

**Duration:** Self-paced (after 90-minute session) or 60 minutes guided  
**Prerequisites:** Docker Desktop with Linux containers (Compose v2+). Git. ~4 GB disk free.  
**Repository:** [industrial-maintenance-copilot](https://github.com/AlmotasemBellahGlal/industrial-maintenance-copilot)

---

## Lab Setup (10 minutes)

### Step 1 — Clone and start the stack

```sh
git clone https://github.com/AlmotasemBellahGlal/industrial-maintenance-copilot.git
cd industrial-maintenance-copilot
cp .env.example .env
docker compose build --builder default api web
docker compose up -d --wait
```

Wait until all services show `Healthy` or `Exited (0)`.

### Step 2 — Ingest the corpus

```sh
docker compose run --rm --no-deps corpus
```

Expected output: `Completed assessment corpus: 31 documents / 150 PDF pages.`

### Step 3 — Get your credentials

```sh
docker compose run --rm --no-deps credentials
```

You will see two tokens — copy the **Supervisor** token. Keep the **Technician** token handy.

### Step 4 — Open the UI

Navigate to **http://127.0.0.1:8080**

Click **Connection** and paste the **Supervisor token**.

---

## Lab Task 1 — Citations and Refusal (10 minutes)

**Topic:** RAG grounding, citation traceability, low-evidence refusal

### Instructions

1. In the UI, navigate to **Ask**. Select:
   - Equipment ID: `11111111-1111-1111-1111-111111111111`
   - Document ID: `22222222-2222-2222-2222-222222222222`
   - Revision ID: `33333333-3333-3333-3333-333333333333`

2. Ask the question: **"What should be done before touching the pump?"**

3. Watch the streaming response. When it completes, find the **citation** shown.

4. Record:
   - The `locator` value (e.g., `text:lines 1-5; scalars 1-193`)
   - The first sentence of the `snippet`

5. Ask a second question in the same conversation: **"What is the recommended dose of aspirin for pump vibration?"**

6. Observe the response.

### Questions to answer

A. What does the `locator` tell you about where the evidence came from?  
B. Why did the second question produce a different type of response than the first?  
C. What would happen if you asked the same question without selecting a specific document?

### Expected lab output

- First question: streaming answer with at least one citation showing exact locator and snippet.
- Second question: `InsufficientEvidence` refusal message — no citation, no fabricated answer.
- Locator format example: `text:lines 1-5; scalars 1-193`

---

## Lab Task 2 — Trace the Agent Pipeline (10 minutes)

**Topic:** Multi-agent orchestration, tool calls, observability, correlation

### Instructions

1. Navigate to **Diagnosis**.

2. Fill in:
   - Equipment ID: `11111111-1111-1111-1111-111111111111`
   - Symptom: `pump vibration`

3. Click **Start Diagnosis**. Watch the live progress indicators. Note which agents appear.

4. After the work order appears, locate the **Trace** view for this run.

5. Count:
   - How many agent steps are shown?
   - Which step shows a tool call (`retrieve_evidence`)?
   - What is the `correlationId` for this run?

6. Navigate to the **Usage** view and search by the same `correlationId`.

### Questions to answer

A. Which of the three agents made a tool call? Why only that one?  
B. What does each agent's step status tell you? (Completed / Failed)  
C. How does the `correlationId` connect the trace to the LLM usage record?  
D. What are the `tokens` and `estimatedCost` values in the usage record? Why?

### Expected lab output

- Trace showing 3 agent steps: SymptomMatcher, DiagnosticSafetyPlanner, WorkOrderGenerator.
- Tool call under SymptomMatcher only.
- Usage record with same correlationId; tokens = null, estimatedCost = null (DemoProvider = synthetic, no billing).
- Work order in PendingApproval state.

---

## Lab Task 3 — Attempt to Bypass the Approval Gate (8 minutes)

**Topic:** Human approval enforcement, server-side security, HTTP status codes

### Instructions

1. After completing Lab Task 2, you should have a work order in **PendingApproval** state.

2. Open browser developer tools (F12) → Network tab.

3. Attempt to dispatch without approving. Click **Dispatch** (or send directly):

```sh
# Replace {id} with your actual work order ID from the trace
curl -X POST http://127.0.0.1:8080/api/work-orders/{id}/dispatch \
  -H "Authorization: Bearer $(cat artifacts/packaging-supervisor.txt)" \
  -H "Content-Type: application/json" \
  -d '{"revision": 1}'
```

4. Record the HTTP status code and response body.

5. Now approve the work order via the UI (**Approve** button).

6. Attempt dispatch again with the same curl command (update the `revision` if needed).

7. Record the new status code and response.

8. Now attempt to complete physical safety verification (click **Verify Safety** for each prerequisite), then dispatch.

### Questions to answer

A. What HTTP status code did you receive before approval? What does it mean?  
B. What changed after approval — why did the second attempt also fail (if it did)?  
C. What is the complete sequence of actions required before dispatch succeeds?  
D. Try the dispatch endpoint using the **Technician** token instead of Supervisor. What do you get?

### Expected lab output

- Pre-approval: **422 Unprocessable Entity** — "dispatch requires approved status"
- Post-approval, pre-verification: **422 Unprocessable Entity** — "mandatory prerequisites not satisfied"
- Post-verification: **200 OK** with dispatch confirmation
- Technician token: **403 Forbidden** — dispatch permission not granted

---

## Lab Task 4 — Modify a Prompt and Observe the Contract Test (10 minutes)

**Topic:** Versioned prompts, contract tests, CI safety net

**Requires .NET 10 SDK** (host install, or run `dotnet test` inside the container if not installed locally).

### Instructions

**Part A — Break the contract:**

1. Open `prompts/symptom-matcher/v1.md` in any text editor.

2. Find the line that starts: `You are the Symptom Matcher.`

3. Change `Symptom Matcher` to `Symptom Finder`.

4. Save the file. Do NOT change the C# source yet.

5. Run:
```sh
dotnet test --filter FullyQualifiedName~PromptVersionTests
```

6. Record what happens.

**Part B — Fix the divergence:**

7. Open `src/IndustrialCopilot.Application/Reasoning/SymptomMatcherAgent.cs`.

8. Find the same phrase and change `Symptom Matcher` to `Symptom Finder` in the C# literal too.

9. Run the test again.

10. Record the result.

**Part C — Revert:**

11. Revert both files to their original text (`Symptom Matcher`) to avoid breaking the build.

### Questions to answer

A. Which specific test failed in Part A? What was the failure message?  
B. What invariant does `PromptVersionTests` enforce?  
C. Why is it important that prompt changes appear as diffs in both files?  
D. How would you version a prompt change correctly? (Hint: `v2.md`)

### Expected lab output

- Part A: `SymptomMatcherRuntimePromptMatchesVersionedArtifact` fails with "prompt drift detected"
- Part B: Test passes after both files updated
- Part C: Both files revert to original text; test passes again

---

## Lab Task 5 — Run the Evaluation and Interpret Results (12 minutes)

**Topic:** RAG evaluation, honest metrics, DemoProvider limitations

**Requires .NET 10 SDK and PostgreSQL** (or use Docker-based evaluation below).

### Option A — Docker-based (no host SDK needed)

```sh
docker compose --profile evaluation build --builder default evaluation
docker compose --profile evaluation up -d --wait evaluation-db
docker compose run --rm --no-deps evaluation --validate
docker compose run --rm --no-deps evaluation --repeat
```

### Option B — Host .NET SDK

```sh
dotnet run --no-build --project tools/IndustrialCopilot.Evaluation -- --validate
# Set EVALUATION_POSTGRES first:
$env:EVALUATION_POSTGRES = "Host=127.0.0.1;Port=5432;Database=maintenance_evaluation;Username=postgres;Password=<password>"
dotnet run --no-build --project tools/IndustrialCopilot.Evaluation -- --repeat
```

### Questions to answer

A. What SHA256 does `--validate` report? Where is this stored in the repository?  
B. Look at the results table. Find `direct-01`. Expected: Refusal. Actual: Answer. Why?  
C. Find `indirect-01`. Expected: Answer. Actual: Answer. Grounding: passed. What makes this case pass when `normal-01-observations` fails?  
D. What does `PASS: deterministic rerun is byte-identical` prove?  
E. If you replaced `CorpusEmbeddings` with `text-embedding-3-small` (OpenAI), which metrics would you expect to improve most?

### Expected lab output

- `--validate`: `Validated 30 cases, 12 adversarial; SHA256 ca023d78c65759125bbee3259d9edeb7f201cc3e72e4db30f847285225f52b38`
- Results table showing: Dense/Hybrid Hit@5 = 9/22 (41%), Keyword = 0/22 (0%)
- `indirect-01`: Answer, True, `citation_and_expected_fact_checks_passed`
- `--repeat`: `PASS: deterministic rerun is byte-identical, including evidence and outcomes.`

---

## Shutting Down

```sh
docker compose down          # preserves data and credentials
# DO NOT use: docker compose down -v  (destroys all data)
```

---

## Troubleshooting

| Problem | Solution |
|---|---|
| Port 8080 in use | Set `WEB_PORT=18080` in `.env`, then restart |
| `up -d --wait` hangs | Check `docker compose ps -a` for failed containers; `docker compose logs migrate seed api` |
| Evaluation SHA mismatch | Never edit `evaluation/golden-v1.json`. Run `--validate` to confirm |
| Credentials lost | If you ran `down -v`, run `up -d --wait` again to regenerate |
| `dotnet test` not found | Install .NET 10 SDK or use Docker evaluation path |
