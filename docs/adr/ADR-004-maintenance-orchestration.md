# ADR-004: Sequential maintenance reasoning with trusted safety gates

- Status: Accepted
- Scope: Issue #17
- Subsequent host/streaming/reconciliation integration: [ADR-006](ADR-006-api-streaming-reconciliation.md). Durable reasoning is covered by [ADR-008](ADR-008-durable-async-reasoning-jobs.md).

## Decision

Three explicit Application agents implement the existing typed interfaces:
Symptom Matcher, Diagnostic & Safety Planner, and Work Order Generator.
Each has a fixed AgentRole, separate focused instructions, typed parsing and
outcomes. Shared code handles bounded LLM transport and JSON parsing only; it
does not act as an unrestricted generic agent.

A Sequential Pipeline orchestrator owns progression:
match -> plan -> deterministic safety assessment -> generation ->
deterministic assessment of the EXACT final scope -> atomic publication ->
stop at human review. Non-success outcomes stop downstream calls.
No approval or dispatch operation exists in agent/orchestration code.

IWorkflowStore now belongs to Application, with Domain aggregate results and
opaque concurrency tokens. PostgresWorkflowStore implements it without copied
persistence logic. TryPublishReviewAsync atomically creates an unapproved
PendingApproval work order, stores its grounded proposal, and changes an
unchanged Running run to WaitingForApproval. Cancellation intent/stale tokens/
duplicate orders prevent publication. Additive migration 002 stores the original
proposal as JSONB: this document-shaped observation retains advisory prerequisites
and exact citations; it is separate from current authoritative Domain scope.
The migration executes only through explicit OperationalSchema.ApplyAsync.

## Evidence and tools

Only SymptomMatcher + AgentTool.RetrieveEvidence permits retrieve_evidence.
Trusted code selects the candidate's exact manual revision and Hybrid retrieval;
the model can supply only a candidate index and query text. Planner/generator
have no tools. Unknown tools/arguments and duplicate tool-call IDs are rejected.
InitialEvidence is not accepted as proof of retrieval: matching must retrieve.

Model JSON references opaque catalog entries. Document/revision/chunk IDs,
locators and snippets are reconstructed from trusted retrieved evidence, never
from generated fields. The parser rejects unknown/duplicate properties, invalid
outcomes, references, collections and ordering. Subsequent agents resolve only
the supplied catalog. This establishes provenance, not a mathematical proof
that natural-language evidence supports every semantic claim.

Default limits: four model turns and four tool calls per agent, TopK five,
eight candidate maximum, 32 output items per collection, 64KiB response text,
bounded evidence sizes, and 60 seconds per agent/policy invocation. Limits are
validated and configurable through ReasoningLimits. Tool execution is read-only.

## Trusted safety policy

ISafetyPolicy is required; null configuration fails construction. The initial
ExactProcedureSafetyPolicy has no permissive fallback. Empty configuration blocks
all work. ApprovedMaintenanceProcedure is trusted deployment code/configuration
binding one equipment/manual revision to exact ordered diagnostic instructions,
exact ordered executable instructions, exact work-order description and the full
authoritative requirement set. Unknown scope or advisory hazards outside the
reviewed requirements block. Omitting advisory prerequisites does not remove any
configured authoritative requirement. Verified requirements are rejected.
An empty mandatory set requires explicit trusted configuration.

The policy is deliberately narrow for a reviewed demo procedure, not general
industrial risk inference. Configurations must be authored/reviewed by competent
operators from applicable procedures; never generate configuration from model
output. There is no shipped universal safety rule. An example construction is:

    new ApprovedMaintenanceProcedure(
        trustedCandidate,
        ["Check vibration"],
        ["Inspect seal"],
        "Inspect isolated pump",
        [new SafetyPrerequisite(trustedRequirementId,
            "Isolate energy and verify zero energy", true)]);

This illustrative procedure is not a production safety checklist. A deployed
procedure must cover the complete authorized scope and site-specific hazards.
The policy is replaceable through its Application port.

Domain AssessSafety installs authoritative definitions and clears verification.
SubmitForApproval prepares human review, never approval. WorkOrder stays
PendingApproval; MaintenanceRun stays WaitingForApproval. Supervisor review and
physical safety verification remain separate trusted operations. Description/
action expansion after planning is caught by the second policy gate.

## Tracing, cancellation and recovery

IRunTraceStore records orchestration, agents, read-only tool calls, LLM calls,
safety gates, outcomes, correlation and returned token usage. Trace names/errors
are fixed safe labels; no prompts, manual text or tool arguments are traced.
[ADR-013](ADR-013-resilience-and-usage-accounting.md) records physical provider attempts
in a separate persisted usage ledger. Unknown usage/pricing stays null; trace links
use execution/step IDs rather than inventing token counts for missing observations.

Caller cancellation propagates as OperationCanceledException. Durable run
cancellation is checked between stages and again atomically at publication;
acknowledgement occurs at that execution boundary. Linked deadlines bound
non-cooperative task waits and late responses cannot advance the pipeline.
Provider-originated cancellation without our token cancellation is a dependency
failure, not an agent timeout. Insufficient evidence/policy rejection block the
run; timeout/technical failure fails it.

The original zero-retry policy is superseded by ADR-013: bounded transient read-only
calls retry within the agent deadline, with provider fallback suppressed. Policy and
storage/dispatch writes are not retried by this loop. Tool turns remain bounded
continuations. Direct repeated run/execution IDs return Conflict; ADR-008 owns durable
job recovery. Eligible technical failure may yield advisory RAG while the run stays Failed.

Cancellation/failure finalization uses a separate five-second cleanup budget.
Storage unavailability or uncertain commit outcomes require host reconciliation:
cancellation is not proof of rollback. Final trace failure cannot undo a
published proposal; the result carries TraceComplete=false. A trace is an
observation, never workflow or approval authority.

## Composition and limits

Register existing LLM, retrieval and operational adapters first, supply a reviewed
ISafetyPolicy, and construct MaintenanceOrchestrator from ILlmProvider,
IRetrievalService, IWorkflowStore, ISafetyPolicy and IRunTraceStore.
The trusted host authenticates/authorizes workflow requests and supplies candidate
identities. Existing OperationalAccessPolicy remains required for trace/review
access. No web host, queue, scheduler, frontend or dispatch adapter is added.

Exact text matching intentionally favors blocking over broad generation. Procedure
coverage, durable resumption, distributed execution scheduling and recovery UI
remain later work. Normal tests use deterministic fakes; PostgreSQL CI tests prove
real retrieval -> orchestration -> atomic publication -> trace persistence.

## Subsequent decisions

ADR-008 supplies durable reasoning jobs. [ADR-013](ADR-013-resilience-and-usage-accounting.md)
supersedes the original no-retry policy for bounded read-only transient attempts and adds advisory
RAG fallback. It does not retry publication/dispatch or weaken exact safety validation.
