# Teaching Pack — Industrial Maintenance Copilot

**Status: COMPLETE (Issue #45)**  
**Topic:** Agentic RAG in Practice: Grounding, Safety, and Human Approval  
**Level:** Postgraduate / Senior Engineer  
**Duration:** 90-minute session + 60-minute self-paced lab

All required teaching deliverables are present in this directory.
Every claim is grounded in the actual implemented repository — no invented features.

---

## Files in This Directory

| File | Description | ITI Requirement |
|---|---|---|
| `slides.md` | 21-slide deck with embedded instructor notes and timing | 15–25 slides |
| `session-plan.md` | 90-min agenda, learning outcomes, assessment mapping, materials checklist | Session plan |
| `lab-guide.md` | 5 hands-on lab tasks with setup instructions and expected outputs | Hands-on lab |
| `answer-key.md` | Complete instructor answers for all lab tasks, discussion questions, and stretch challenges | Answer key |
| `stretch-challenges.md` | 3 advanced extension challenges with full design context | ≥3 stretch challenges |
| `misconceptions.md` | One-page printable handout: 5 common misconceptions | Misconceptions page |

---

## Session Topic

**"Agentic RAG in Practice: Grounding, Safety, and Human Approval"**

This topic was chosen because it represents the most technically distinctive aspect of
this repository and addresses a genuine gap in existing AI engineering curricula.

The session teaches:
- RAG ingestion, chunking, embeddings, and retrieval (keyword, dense, hybrid)
- What makes a pipeline "agentic" — specialized roles, tool restrictions, typed contracts
- Why safety gates must be code, not prompts (OWASP LLM Top 10 defence)
- How human approval is enforced by a state machine, not a conversation
- Why long-running AI workflows require durable async jobs (T7 pattern)
- How to measure and honestly report RAG evaluation results

---

## Learning Outcomes (6)

By the end of the session, students will be able to:

1. Explain the difference between a plain RAG pipeline and an agentic RAG pipeline.
2. Describe Clean Architecture dependency rules and why they enable testable AI systems.
3. Trace the complete data flow from diagnosis request through three agents to dispatch.
4. Identify the trust boundary for retrieved content and the prompt injection defence.
5. Explain why T7 durable jobs are necessary and how lease-based recovery works.
6. Run the FR-3 evaluation, interpret metrics honestly, and identify what they actually measure.

---

## Quick Reference — Lab Setup

```sh
git clone https://github.com/AlmotasemBellahGlal/industrial-maintenance-copilot.git
cd industrial-maintenance-copilot
cp .env.example .env
docker compose build --builder default api web
docker compose up -d --wait
docker compose run --rm --no-deps credentials
docker compose run --rm --no-deps corpus
# Open http://127.0.0.1:8080
```

Full lab instructions: `teaching/lab-guide.md`

---

## What Requires Human Action

The following are NOT in this directory — they must be created by the instructor:

| Item | Notes |
|---|---|
| **Product demo video** (5–8 min) | Screen recording of the running system demonstrating the 5-Minute Demo Path from `README.md` |
| **Teaching sample video** (10 min) | Face + voice recording, required for ITI submission; topic: any section from this session plan |
| **Slide deck in presentation format** | Convert `slides.md` to PowerPoint/Google Slides/reveal.js as appropriate for your delivery format |
| **Video links in README.md** | Add to root `README.md` once recordings are hosted |

---

## Assessment Mapping

| Learning Outcome | Lab Evidence | Stretch Challenge |
|---|---|---|
| 1. RAG vs Agentic RAG | Task 1: Citation locator + refusal | — |
| 2. Clean Architecture | Task 4: Prompt contract test | Stretch 1: Agent design |
| 3. End-to-end trace | Task 2: Trace view, correlationId | — |
| 4. Trust boundary | Task 3: 422 + 403 responses | — |
| 5. T7 durability | Task 2 + demo | Stretch 2: Two Workers |
| 6. Honest evaluation | Task 5: SHA256 + metric table | Stretch 3: Real embeddings |
