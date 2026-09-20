# Business Requirements Document (BRD)

## Industrial Maintenance Copilot

**Domain:** D5 — Industrial / Field Maintenance
**Mandatory Twist:** T7 — Async Long-Running Jobs
**Status:** Draft

---

## 1. Business Context

Industrial maintenance teams rely on equipment manuals, maintenance procedures, and safety documentation when diagnosing equipment issues and preparing maintenance work.

Technicians may need to search across multiple documents and manual revisions to identify the correct equipment information, determine an appropriate diagnostic sequence, verify required safety prerequisites, and prepare a work order.

The Industrial Maintenance Copilot is intended to assist this process by providing document-grounded maintenance guidance and supporting a structured diagnostic workflow while keeping consequential actions under human supervision.

---

## 2. Problem Statement

Maintenance technicians can spend significant time searching and cross-referencing specialised equipment documentation when investigating reported symptoms.

The process becomes more difficult when multiple equipment models or manual revisions exist. Using information from the wrong manual revision, relying on unsupported information, or missing a required safety prerequisite can lead to incorrect maintenance guidance and operational risk.

The system therefore needs to help technicians locate relevant information from the approved document corpus, preserve traceability to the original source, and support the maintenance workflow without independently authorising consequential actions.

---

## 3. Personas

### 3.1 Maintenance Technician

**Description:**
A maintenance professional responsible for investigating reported equipment symptoms and preparing maintenance work.

**Primary needs:**
- Describe an equipment symptom.
- Ask questions about approved maintenance documentation.
- Receive answers grounded in the document corpus.
- View citations to the supporting source.
- Start a diagnostic workflow.
- Track the progress of long-running diagnostic jobs.
- Review diagnostic guidance and generated draft work orders.

**Key limitation:**
The technician cannot use the Copilot to bypass required safety controls or supervisor approval.

---

### 3.2 Maintenance Supervisor

**Description:**
A senior maintenance professional responsible for reviewing consequential maintenance actions before dispatch.

**Primary needs:**
- Review the diagnostic outcome.
- Review identified safety prerequisites.
- Review the generated draft work order.
- Approve a work order.
- Reject a work order.
- Edit and approve a work order.
- Review the audit trail associated with the workflow.

**Key responsibility:**
A work order cannot be dispatched through the workflow without explicit supervisor approval.
---

## 4. Business Objectives and Success Criteria

### BO-01 — Provide Grounded Maintenance Assistance

**Objective:**
Help maintenance technicians retrieve relevant information from the approved document corpus without relying on unsupported model knowledge.

**Success criteria:**
- Supported factual answers include structured citations traceable to the supporting source chunks.
- Questions with insufficient supporting evidence result in an explicit refusal rather than an unsupported answer.
- Retrieval quality is measured using the project's evaluation dataset.

---

### BO-02 — Support a Safety-Constrained Diagnostic Workflow

**Objective:**
Assist technicians through equipment diagnosis while ensuring that required safety prerequisites cannot be bypassed by AI-generated workflow decisions.

**Success criteria:**
- Required safety prerequisites are represented and validated by deterministic application logic.
- A workflow cannot proceed to work-order dispatch when required safety prerequisites have not been satisfied.
- Safety enforcement does not depend solely on an LLM prompt or generated text.

---

### BO-03 — Preserve Human Control Over Consequential Actions

**Objective:**
Ensure that AI-generated maintenance recommendations remain advisory until reviewed by an authorised human supervisor.

**Success criteria:**
- 100% of generated work orders require explicit supervisor approval before dispatch.
- The approval workflow supports approve, reject, and edit-and-approve actions.
- Approval decisions are recorded in the workflow audit trail.

---

### BO-04 — Provide End-to-End Traceability

**Objective:**
Allow completed and active AI workflows to be inspected for operational review, debugging, and accountability.

**Success criteria:**
- Every workflow execution receives a unique Run ID.
- The system records the agents, tools, retrieved chunks, and approval actions associated with a run.
- A stored run can be inspected after execution.

---

### BO-05 — Support Reliable Long-Running AI Workflows

**Objective:**
Execute long-running diagnostic workflows without requiring the initiating HTTP request to remain open.

**Success criteria:**
- Workflow submission returns without waiting for the full workflow to complete and provides a Job ID.
- Job progress is communicated to the client.
- Persisted jobs can survive application restart.
- Jobs support cancellation and resumable processing.
- Job execution is idempotent so retries do not duplicate consequential side effects.
---

## 5. Business Requirements

### BR-01 — Maintenance Document Ingestion

**Requirement:**
The system shall allow authorised users to ingest approved maintenance documents into the searchable maintenance corpus.

**Business value:**
Maintenance guidance must be derived from controlled documentation rather than unsupported model knowledge.

**Acceptance criteria:**
- The system accepts at least two supported document formats.
- Each ingested document retains identifying metadata including its source and version where available.
- The system reports whether document ingestion succeeded or failed.
- Re-ingesting the same document does not create unintended duplicate indexed content.
### BR-02 — Grounded Maintenance Question Answering

**Requirement:**
The system shall allow an authenticated maintenance user to ask natural-language questions against the approved maintenance document corpus.

**Business value:**
Technicians can locate relevant maintenance information without manually searching across multiple documents.

**Acceptance criteria:**
- The system retrieves evidence from the approved corpus before generating a supported answer.
- Supported factual answers include citations traceable to the source document and exact supporting chunk.
- The answer does not present unsupported model knowledge as document-grounded fact.
- When available evidence is insufficient, the system explicitly indicates that there is not enough information in the corpus.
### BR-03 — Diagnostic Workflow Initiation

**Requirement:**
The system shall allow an authenticated maintenance technician to start a diagnostic workflow from a reported equipment symptom.

**Business value:**
Technicians receive structured assistance through the maintenance investigation process rather than only isolated question-and-answer responses.

**Acceptance criteria:**
- The technician can provide an equipment identifier and reported symptom.
- Starting the workflow creates a uniquely identifiable workflow run.
- The workflow attempts to identify the relevant equipment and applicable manual revision.
- The workflow produces a diagnostic outcome based on retrieved corpus evidence.
- Workflow failure is reported without silently fabricating a diagnostic result.
### BR-04 — Equipment and Manual Revision Identification

**Requirement:**
The diagnostic workflow shall identify the relevant equipment and applicable maintenance manual revision before producing equipment-specific diagnostic guidance.

**Business value:**
Maintenance decisions based on an incorrect equipment model or document revision may produce invalid or unsafe guidance.

**Acceptance criteria:**
- Retrieved equipment-specific guidance is associated with identifiable equipment metadata.
- Manual revision metadata is retained and available to the workflow.
- The workflow does not silently substitute guidance from a different equipment model when the requested equipment cannot be reliably identified.
- Ambiguous or insufficient equipment/version information is surfaced rather than guessed.
### BR-05 — Diagnostic and Safety Planning

**Requirement:**
The system shall produce a structured diagnostic sequence together with the safety prerequisites supported by the applicable maintenance documentation.

**Business value:**
Technicians need both diagnostic guidance and the safety conditions that govern how maintenance steps may be performed.

**Acceptance criteria:**
- Diagnostic steps are linked to supporting corpus evidence.
- Identified safety prerequisites are clearly distinguishable from ordinary diagnostic steps.
- Missing evidence for a safety-critical instruction is not silently inferred.
- Required safety prerequisites are passed to deterministic application validation before the workflow can progress to a dispatchable work order.
### BR-06 — Draft Work Order Generation

**Requirement:**
The system shall generate a draft maintenance work order from the approved diagnostic workflow output.

**Business value:**
Converting validated diagnostic findings into a structured draft reduces repetitive manual preparation while preserving human review.

**Acceptance criteria:**
- The generated work order is initially marked as a draft or pending approval.
- The work order references the relevant equipment and reported symptom.
- The work order contains the proposed maintenance actions.
- Applicable safety prerequisites are included or referenced.
- Generation of a draft does not constitute approval or dispatch.
### BR-07 — Supervisor Approval Gate

**Requirement:**
The system shall require explicit approval from an authorised maintenance supervisor before a generated work order can be dispatched.

**Business value:**
Consequential maintenance actions remain under accountable human control.

**Acceptance criteria:**
- A pending work order cannot be dispatched before supervisor approval.
- An authorised supervisor can approve the work order.
- An authorised supervisor can reject the work order.
- An authorised supervisor can edit and approve the work order.
- Each approval decision is recorded with its associated workflow run.
### BR-08 — Asynchronous Workflow Execution

**Requirement:**
Long-running diagnostic workflows shall execute asynchronously without requiring the initiating client request to remain open until completion.

**Business value:**
Users can start computationally expensive AI workflows without blocking the application while the full workflow executes.

**Acceptance criteria:**
- Starting a long-running workflow returns a Job ID without waiting for full workflow completion.
- Jobs are processed through a queue and worker mechanism.
- The client can observe job progress.
- A persisted job can recover after application or worker restart.
- A running job can be cancelled.
- Interrupted jobs support resumable processing.
- Reprocessing a job does not duplicate consequential side effects.
### BR-09 — Workflow Traceability

**Requirement:**
The system shall maintain an inspectable record of each diagnostic workflow execution.

**Business value:**
Maintenance supervisors and system operators need to understand how AI-assisted outcomes were produced and investigate failures or questionable results.

**Acceptance criteria:**
- Each workflow has a unique Run ID.
- The trace records the specialised agents executed during the run.
- Tool invocations associated with the run are recorded.
- Retrieved supporting chunks are traceable from the run.
- Human approval actions are associated with the run.
- The completed trace remains inspectable after workflow execution.### BR-09 — Workflow Traceability

**Requirement:**
The system shall maintain an inspectable record of each diagnostic workflow execution.

**Business value:**
Maintenance supervisors and system operators need to understand how AI-assisted outcomes were produced and investigate failures or questionable results.

**Acceptance criteria:**
- Each workflow has a unique Run ID.
- The trace records the specialised agents executed during the run.
- Tool invocations associated with the run are recorded.
- Retrieved supporting chunks are traceable from the run.
- Human approval actions are associated with the run.
- The completed trace remains inspectable after workflow execution.
### BR-10 — Authentication and Role-Based Authorisation

**Requirement:**
The system shall authenticate users and enforce role-based permissions for maintenance technicians and maintenance supervisors.

**Business value:**
Consequential maintenance actions must only be available to authorised users with the appropriate responsibility.

**Acceptance criteria:**
- The system supports at least the Technician and Supervisor roles.
- Protected operations require an authenticated user.
- Technician and Supervisor permissions are enforced by the server.
- A Technician cannot perform Supervisor-only approval actions.
- Unauthorised requests are rejected even when sent directly to the API.

---

### BR-11 — Live Workflow Progress

**Requirement:**
The system shall provide live progress updates while asynchronous diagnostic workflows are executing.

**Business value:**
Users need visibility into long-running workflows instead of waiting without knowing the current execution state.

**Acceptance criteria:**
- The client can receive workflow progress without repeatedly polling for every update.
- Progress identifies meaningful workflow stages, such as retrieval, diagnostic planning, safety validation, and work-order generation.
- The final workflow state indicates successful completion, failure, or cancellation.
- Progress updates do not bypass the persisted job state as the authoritative execution record.

---

### BR-12 — Workflow Cancellation

**Requirement:**
The system shall allow an authorised user to request cancellation of an active long-running workflow.

**Business value:**
Users must be able to stop work that is no longer required without leaving the workflow in an unknown state.

**Acceptance criteria:**
- An authorised user can request cancellation of an active job.
- Cancellation is propagated to running application and model operations where technically supported.
- The job reaches a clearly identifiable cancelled state.
- Cancellation does not trigger pending consequential side effects.
### BR-13 — Session and Workflow History

**Requirement:**
The system shall preserve user-visible history for maintenance questions and diagnostic workflows.

**Business value:**
Users need to revisit previous maintenance interactions and workflow outcomes without repeating the complete process.

**Acceptance criteria:**
- An authenticated user can view relevant previous sessions or workflow runs.
- Stored history identifies the associated workflow or session.
- Previous grounded answers retain their citations.
- Previous workflow outcomes retain their final status.
- Access to stored history respects server-side authorisation rules.

---

### BR-14 — Operational Observability

**Requirement:**
The system shall provide sufficient operational telemetry to trace requests and AI workflow execution across system components.

**Business value:**
Operators need to diagnose failures, investigate unexpected AI behaviour, and understand the operational cost of AI-assisted workflows.

**Acceptance criteria:**
- Requests and workflow executions use correlation identifiers.
- Logs contain sufficient context to associate API, worker, agent, and tool activity with the same workflow.
- Model token usage is recorded where the provider exposes it.
- Estimated model cost can be derived or recorded where applicable.
- Agent and tool execution is traceable.
- The system exposes health information for important runtime dependencies.

---

### BR-15 — RAG and Workflow Evaluation

**Requirement:**
The project shall include a repeatable evaluation process for grounded retrieval and AI-assisted maintenance behaviour.

**Business value:**
The quality of AI-assisted maintenance guidance must be measured rather than judged only through manual demonstration.

**Acceptance criteria:**
- A golden evaluation dataset contains at least 25 question-and-answer cases.
- At least 5 evaluation cases are adversarial or safety-focused.
- The evaluation covers retrieval quality.
- The evaluation measures citation correctness.
- The evaluation measures grounded-answer quality.
- The evaluation includes correct-refusal cases where the corpus does not provide sufficient evidence.
- Evaluation results can be reproduced and documented.

---

### BR-16 — AI and Application Security Controls

**Requirement:**
The system shall apply security controls to both conventional application flows and LLM/agent-specific workflows.

**Business value:**
Retrieved documents, user prompts, model output, and agent tools introduce security risks that must not be trusted implicitly.

**Acceptance criteria:**
- Retrieved document content is treated as untrusted input rather than system instruction.
- Tool arguments generated or proposed by AI are validated against typed schemas before execution.
- Agents are restricted to explicitly allowed tools.
- Sensitive operations remain protected by authentication, authorisation, and human approval where applicable.
- The system includes protections or tests for direct and indirect prompt injection.
- Secrets are not stored in source control.
- Application inputs are validated at trust boundaries.
- Dependency and secret scanning are included in the engineering workflow.
---

## 6. Business Rules

### BRULE-01 — Supervisor Approval Is Mandatory

No AI-generated work order may be dispatched without explicit approval from an authorised maintenance supervisor.

The approval decision must be enforced by application logic and must not depend on an LLM-generated statement indicating that approval has occurred.

---

### BRULE-02 — Safety Prerequisites Cannot Be Bypassed

A work order that requires safety prerequisites must not progress to dispatch unless those prerequisites have been validated by deterministic application logic.

Safety enforcement must not rely solely on prompts or model-generated text.

---

### BRULE-03 — Corpus Evidence Is the Source of Maintenance Guidance

The system must not present unsupported LLM knowledge as document-grounded maintenance guidance.

When sufficient supporting evidence cannot be retrieved from the approved corpus, the system must communicate that there is not enough information in the corpus.

---

### BRULE-04 — Equipment and Manual Revision Must Remain Traceable

Equipment-specific maintenance guidance must retain traceability to the equipment and manual revision from which the supporting evidence was retrieved.

The system must not silently substitute another equipment model or manual revision when the requested information cannot be reliably identified.

---

### BRULE-05 — Consequential Side Effects Must Be Idempotent

Retrying, resuming, or reprocessing an asynchronous job must not cause the same consequential action, such as work-order dispatch, to be executed more than once.

---

### BRULE-06 — Authorisation Is Enforced Server-Side

Permissions for technician and supervisor actions must be enforced by the server and must not rely solely on hiding or disabling user-interface controls.
---

## 7. Assumptions and Constraints

### Assumptions

- **A-01:** The MVP operates on public or synthetic maintenance documents and does not contain real personal data.
- **A-02:** Equipment identifiers and manual revision metadata required by the demo are available within the application's controlled dataset.
- **A-03:** Maintenance documentation used by the MVP is considered the approved corpus for demonstration purposes.
- **A-04:** A maintenance supervisor is available to review consequential work-order actions.
- **A-05:** The MVP demonstrates the workflow in a controlled environment and does not directly control physical industrial equipment.

### Constraints

- **C-01:** The project must use the assigned D5 Industrial / Field Maintenance domain.
- **C-02:** The project must implement the assigned T7 Async Long-Running Jobs twist.
- **C-03:** The corpus must contain at least 30 documents and 150 pages using public or synthetic data.
- **C-04:** No real personal data may be used.
- **C-05:** The implementation is scoped to the assessment time window and prioritises a demonstrable, well-engineered MVP over production-scale infrastructure.
---

## 8. Business and Operational Risks

| ID | Risk | Potential Impact | Planned Mitigation |
|---|---|---|---|
| R-01 | Incorrect or irrelevant document retrieval | Incorrect maintenance guidance | Hybrid retrieval, metadata-aware retrieval, citations, and retrieval evaluation |
| R-02 | Wrong equipment or manual revision selected | Guidance may not apply to the actual equipment | Preserve equipment/version metadata and refuse or surface ambiguity when identification is unreliable |
| R-03 | Safety prerequisite omitted by the model | Unsafe maintenance workflow | Deterministic safety validation outside the LLM and mandatory safety gate |
| R-04 | LLM produces unsupported information | User may trust fabricated maintenance guidance | Grounded generation, citations, low-evidence refusal, and evaluation |
| R-05 | Supervisor approval is bypassed | Consequential AI action occurs without human control | Server-side approval state enforcement |
| R-06 | Async job is processed more than once | Duplicate side effects or inconsistent workflow state | Idempotent job processing and persisted job state |
| R-07 | Worker/API restarts during a workflow | Workflow is lost or left inconsistent | Persistent queue/job state and resumable processing |
| R-08 | Prompt injection exists inside an ingested document | Retrieved content may attempt to manipulate the AI workflow | Separate trusted instructions from retrieved content, restrict agent tools, and evaluate indirect prompt-injection cases |
---

## 9. Scope

### 9.1 In Scope

The MVP includes:

- Ingestion of approved maintenance documents.
- Search and retrieval across the maintenance corpus.
- Grounded question answering with traceable citations.
- Equipment and manual-revision-aware diagnostic assistance.
- Multi-agent diagnostic workflow.
- Diagnostic and safety planning.
- Deterministic enforcement of required safety prerequisites.
- Draft work-order generation.
- Supervisor approval, rejection, and edit-and-approve workflow.
- Asynchronous job execution using a queue and workers.
- Live job and agent progress reporting.
- Job cancellation, recovery, resumability, and idempotent processing.
- Authentication and role-based authorisation.
- Workflow traceability and audit information.
- RAG evaluation and security testing required by the assessment.
- Minimal user interface required to demonstrate the workflow.

### 9.2 Out of Scope

The MVP does not include:

- Direct control of physical industrial equipment.
- Integration with real factory PLC, SCADA, or IoT systems.
- Predictive maintenance based on live sensor telemetry.
- Integration with a production ERP or CMMS.
- Automated spare-parts purchasing or inventory management.
- Fully autonomous maintenance decisions without human approval.
- Production-scale multi-region deployment.
- Native mobile applications.
- Rich visual/UI design beyond what is required to demonstrate the assessment workflow.

---

## 10. Requirements Traceability Matrix

**Updated:** Issue #41, 2026-09-18. All rows updated from "Planned" to reflect actual
implementation status. Evidence paths reference committed code, tests, and CI jobs.

| Requirement | Primary Objective | Status | Evidence (file / test / CI step) |
|---|---|---|---|
| BR-01 Maintenance Document Ingestion | BO-01 | **Implemented** | `tools/IndustrialCopilot.Corpus/` (31 docs / 150 PDF pages); `ManualIngestionService`; idempotent re-ingestion proven by `corpus` command × 2 in CI `validate` job; `IndustrialCopilot.Infrastructure.Tests` ingestion integration tests; `knowledge.ingestion_attempts` migration 002 |
| BR-02 Grounded Maintenance Q&A | BO-01 | **Implemented** | `AskService` + `IRetrievalService`; `EvidenceCatalog` validates citation provenance; `product-smoke.mjs` proves exact `documentId`/`revisionId`/`locator`/`snippet`; `InsufficientEvidence` refusal proven; ADR-012 |
| BR-03 Diagnostic Workflow Initiation | BO-02, BO-04 | **Implemented** | `/api/jobs` 202 + `/api/runs`; `MaintenanceOrchestrator`; `operations.runs` migration 001; `t7-smoke.mjs` end-to-end proof; `IndustrialCopilot.IntegrationTests` |
| BR-04 Equipment / Manual Revision Identification | BO-01, BO-02 | **Implemented** | `EquipmentManualCandidate` with `documentId`/`manualRevisionId` on every retrieval; `EvidenceCatalog.Resolve` validates provenance; `AgentContractTests.EvidenceMustMatchDocumentAndRevision` |
| BR-05 Diagnostic and Safety Planning | BO-02 | **Implemented** | `DiagnosticSafetyPlannerAgent`; `ISafetyPolicy.AssessAsync` (deterministic, twice); `WorkOrder.AssessSafety`; `demo-smoke.mjs` safety gate assertions |
| BR-06 Draft Work Order Generation | BO-03 | **Implemented** | `WorkOrderGeneratorAgent`; `WorkOrder` initial status `PendingApproval`; `operations.work_orders` + `work_order_snapshots`; `IndustrialCopilot.Domain.Tests` |
| BR-07 Supervisor Approval Gate | BO-03 | **Implemented** | `PostgresWorkOrderApprovalService`; dispatch returns 422 before approval; `demo-smoke.mjs` proves 422 + approve + verify + dispatch; `scripts/security/demo_roles.py` CI proof |
| BR-08 Asynchronous Workflow Execution | BO-05 | **Implemented** | `operations.reasoning_jobs` + lease recovery (migration 005); `t7-smoke.mjs --submit-recovery` / `--verify-recovery`; ADR-008; CI `validate` step "Durable reasoning process crash, recovery, cancellation and bilingual safety proof" |
| BR-09 Workflow Traceability | BO-04 | **Implemented** | `WorkflowTrace` + `PostgresRunTraceStore` + `operations.traces`; `/api/traces/{executionId}`; `demo-smoke.mjs` asserts 3 agent steps + correlation ID per run |
| BR-10 Authentication and Role-Based Authorisation | BO-03 | **Implemented** | `HostAuthentication` constant-time compare; `MaintenanceEndpoints.Permit`; `scripts/security/demo_roles.py` CI; Technician 403/404 proven in `product-smoke.mjs` |
| BR-11 Live Workflow Progress | BO-05 | **Implemented** | `/api/jobs/{id}/events` SSE; `MaintenanceProgressKind` enum (WorkflowStarted, AgentStarted, AgentCompleted, SafetyEvaluated, WaitingForApproval, etc.); `t7-smoke.mjs` reads SSE events |
| BR-12 Workflow Cancellation | BO-05 | **Implemented** | `/api/jobs/{jobId}/cancel`; `DurableCancellationException`; `product-smoke.mjs` proves SSE cancel + state=3 in history; `IndustrialCopilot.IntegrationTests` |
| BR-13 Session and Workflow History | BO-04 | **Implemented** | `PostgresConversationStore` migration 006; `/api/conversations`; `packaging-smoke.mjs --verify-restart` proves history + citations survive restart; Technician isolation 404 proven |
| BR-14 Operational Observability | BO-04 | **Implemented** | `AccountedLlmProvider` + `PostgresLlmUsageStore` migration 007; `/api/usage`; `/health/live` + `/health/ready`; `packaging-smoke.mjs` proves usage records per correlation ID |
| BR-15 RAG and Workflow Evaluation | BO-01, BO-02 | **Implemented** | `evaluation/golden-v1.json` (30 cases, 12 adversarial); `IndustrialCopilot.Evaluation`; `--validate` + `--repeat` (byte-identical) in CI; `docs/EVALUATION.md`; honest poor scores documented |
| BR-16 AI and Application Security Controls | BO-02, BO-03 | **Implemented** | `AgentRuntime` UNTRUSTED DATA boundary; `AgentJson.Shape` tool argument validation; `scripts/security/scan.py` (gitleaks + NuGet/npm) in CI `security` job; `docs/SECURITY.md` OWASP matrix; direct/indirect injection cases in `golden-v1.json` |