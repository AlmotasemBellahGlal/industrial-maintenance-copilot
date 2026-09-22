# Stretch Challenges
## Agentic RAG in Practice — Advanced Extensions

These three challenges are for students who complete the five main lab tasks early
or who want a deeper technical engagement. Each extends the core architecture in a
direction that reveals real engineering trade-offs.

**Instructor notes:** Expected answers and full solutions are in `teaching/answer-key.md`.

---

## Stretch Challenge 1 — Design a Fourth Agent: Impact Assessor

**Difficulty:** Conceptual + light implementation  
**Time estimate:** 30–45 minutes  
**Prerequisites:** Lab Tasks 2 and 3 completed

### Background

The current pipeline has three agents: SymptomMatcher, DiagnosticSafetyPlanner,
WorkOrderGenerator. The Safety Policy checks whether the *plan* is safe, but it does
not assess the *operational risk* of **deferring** the maintenance.

A technician might safely isolate and inspect a pump, but if the same pump has failed
four times in the last 30 days, deferral (choosing not to act now) may be very high risk.

### The Challenge

Design — and optionally partially implement — a fourth agent: **`ImpactAssessorAgent`**.

**Required answers (written design document or code sketch):**

1. **Where in the pipeline does it run?**  
   Justify its position relative to SymptomMatcher, DiagnosticSafetyPlanner,
   WorkOrderGenerator, and the two `ISafetyPolicy.AssessAsync()` calls.

2. **What tool(s) would it need?**  
   Define the tool name, its JSON Schema arguments, and what it would return.
   Where would this data come from in the existing database schema?

3. **What change does `AgentRuntime.Allows()` need?**  
   Write the updated method. How does this guarantee the new agent cannot call
   tools it isn't authorised for?

4. **Would its output require a new deterministic safety check?**  
   If so, describe the new `ISafetyPolicy` implementation. If not, explain why not.

5. **What new Application port (interface) would it depend on?**  
   Name it, give its signature, and explain which existing Infrastructure adapter
   it would build on.

6. **Anti-hallucination constraint:** Write the system prompt fragment that prevents
   the agent from inventing maintenance history it cannot retrieve.

**Bonus:** Sketch the `ImpactAssessmentResult` record. What fields does it contain?
What invariants must it satisfy (think about `AgentContractTests` patterns)?

---

## Stretch Challenge 2 — Run Two Workers Simultaneously (Lease Exclusion Proof)

**Difficulty:** Operational + observational  
**Time estimate:** 20–30 minutes  
**Prerequisites:** Docker stack running, Lab Task 2 completed

### Background

The `ReasoningWorker` uses PostgreSQL lease-based exclusion — `SELECT ... FOR UPDATE SKIP LOCKED` —
to claim jobs without a dedicated message broker. The architecture claims it supports
multiple concurrent Workers. Prove it.

### The Challenge

**Step 1 — Configure two Worker replicas:**

Edit `compose.yaml` to run two Worker instances. You can either:
- Add `deploy: { replicas: 2 }` under `worker:`, OR
- Start a second Worker container manually:
  ```sh
  docker compose run -d --no-deps --name worker-2 worker
  ```

**Step 2 — Submit two simultaneous jobs:**

Using the UI or `curl`, submit two diagnosis jobs for the same equipment at roughly
the same time:
```sh
# Terminal 1:
curl -X POST http://127.0.0.1:8080/api/jobs \
  -H "Authorization: Bearer $(cat artifacts/packaging-supervisor.txt)" \
  -H "Content-Type: application/json" \
  -H "Idempotency-Key: job-a-$(date +%s)" \
  -d '{"equipmentId":"11111111-1111-1111-1111-111111111111","symptom":"pump vibration"}'

# Terminal 2 (simultaneously):
curl -X POST http://127.0.0.1:8080/api/jobs \
  -H "Authorization: Bearer $(cat artifacts/packaging-supervisor.txt)" \
  -H "Content-Type: application/json" \
  -H "Idempotency-Key: job-b-$(date +%s)" \
  -d '{"equipmentId":"11111111-1111-1111-1111-111111111111","symptom":"pump vibration"}'
```

**Step 3 — Observe and answer:**

A. Look at `docker compose logs worker` and `docker compose logs worker-2`.
   Which Worker processed which job? How can you tell?

B. Submit just ONE job. Which Worker picks it up? Why?

C. What happens if `leaseSeconds = 1` and the processing takes 5 seconds?
   Change `leaseSeconds` in `ContainerDemo.cs` line `new ReasoningSchedule(equipment, leaseSeconds: 1)`
   and re-run. What do you observe in the job's `attempts` count?

D. Restore `leaseSeconds: 15` before finishing.

**The key question:** Explain in one paragraph why the PostgreSQL lease approach
provides equivalent correctness guarantees to a dedicated message broker for this
use case, and what trade-offs it accepts.

---

## Stretch Challenge 3 — Switch to a Real Embedding Provider

**Difficulty:** Configuration + measurement  
**Time estimate:** 45–60 minutes (plus API key cost: ~$0.01 USD)  
**Prerequisites:** OpenAI API key, Lab Task 5 completed

### Background

The current evaluation uses `CorpusEmbeddings` — 256-dimensional deterministic hash
buckets. Dense Hit@5 is 41%. The architecture's `ILlmProvider` abstraction means you can
swap the embedding provider without touching Application or Domain code.

### The Challenge

**Step 1 — Create an isolated environment:**

```sh
# Copy .env.example to a new file
cp .env.example .env.openai

# Edit .env.openai:
COMPOSE_PROJECT_NAME=maintenance-openai
DEMO_PROVIDER=OpenAi
OPENAI_API_KEY=<your-openai-api-key>
CHAT_MODEL=gpt-4o-mini
EMBEDDING_MODEL=text-embedding-3-small
EMBEDDING_PROFILE=openai-3small-v1
EMBEDDING_DIMENSIONS=1536
```

**Step 2 — Start a fresh isolated stack:**

```sh
docker compose --env-file .env.openai build --builder default api web
docker compose --env-file .env.openai up -d --wait
docker compose --env-file .env.openai run --rm --no-deps corpus
```

**Step 3 — Run the evaluation against the new stack:**

```sh
# Build the evaluation image for the new project
docker compose --env-file .env.openai --profile evaluation build --builder default evaluation
docker compose --env-file .env.openai --profile evaluation up -d --wait evaluation-db
docker compose --env-file .env.openai run --rm --no-deps evaluation --repeat
```

**Step 4 — Compare and answer:**

A. Record the new Dense Hit@5, Hybrid Hit@5, and MRR@5. Fill in the comparison table:

| Metric | 256-dim hash | 1536-dim semantic | Delta |
|---|---|---|---|
| Dense Hit@5 | 41% | ? | ? |
| Hybrid Hit@5 | 41% | ? | ? |
| Dense MRR | 0.37 | ? | ? |
| Keyword Hit@5 | 0% | ? | ? |

B. Why is Keyword Hit@5 unchanged even with a better embedding model?

C. Explain why you **cannot** use the `maintenance-openai` index with the original
   `maintenance` stack (or vice versa). What specific error would the system throw
   if you tried?

D. The `--repeat` check still passes (byte-identical). Why does determinism hold even
   though OpenAI's API is non-deterministic for chat completions?

E. Delete the isolated stack:
```sh
docker compose --env-file .env.openai --profile evaluation down -v
```
Why is `-v` acceptable here but NOT acceptable for the main demo stack?
