# ADR-005: Trusted tools and durable dispatch reservations

Status: Accepted for Issue #19

Issue #21 supplies the trusted API host and reconciliation scheduling described as future work here; see [ADR-006](ADR-006-api-streaming-reconciliation.md).

## Problem

A database rollback cannot undo external acceptance. Checking approval, sending,
then saving Dispatched leaves a race with edits/cancellation and an ambiguous
failure window. A model's tool request, actor string, or safety assertion is not
authorization.

## Decision

Application owns `DispatchCoordinator`, `IDispatchAttemptStore`, `IExternalDispatch`,
`IActionAuthorization` and the fixed trusted-tool executor. Domain lifecycle rules
remain unchanged. Infrastructure implements PostgreSQL reservation, receiver and
approval/verification persistence. No transport or database types flow inward.

Reserve under run-then-work-order locks, checking current revision, concurrency
token, approved status, current assessment, all mandatory verifications, and an
active non-cancelling run. Human consent must also have been recorded by the
trusted approval service: a restored approval alone is insufficient. Compare the
immutable reviewed content, requirement definitions and decision against current
state. Physical verification may subsequently be recorded by an independently
authorized actor. Imported/legacy approvals without this provenance need a fresh
trusted review, normally of a new revision; migration does not invent consent.

Persist a unique attempt per (WorkOrderId, revision), with a UUID idempotency key,
requested/current reserved snapshot tokens, run/token, requester, timestamp and
audit. Reservation rotates work-order/run tokens so racing optimistic saves fail.
Only one outstanding attempt may bind a run. Pending and Uncertain freeze workflow
saves, edited approval, assessment/verification changes, and run cancellation or
publication. Ordinary workflow saves cannot create or rewrite Dispatched state.
Detached aggregates can still change locally; none of those changes can persist
over the reservation. This is an Application storage invariant, not a new Domain
status, prompt instruction, or in-memory lock.

States:

* Pending: reserved, not yet finalized. `InvocationStarted` is durable before send.
* Confirmed: accepted externally, Domain Dispatch and optional active-run completion
  committed atomically with the attempt and audit.
* DefinitivelyFailed: certified non-acceptance, including no outstanding call that
  may later accept. Freeze released. Explicit retry requires unchanged reserved
  work-order/run tokens and a fresh gate/authorization check; same UUID is reused.
* Uncertain: acceptance unknown. Freeze retained. Reconcile by the existing UUID,
  never resend as a new attempt. Missing receiver records alone are not proof of
  non-acceptance.

A per-attempt PostgreSQL session advisory lock serializes send/reconcile/confirm
across processes, without holding a business transaction during external I/O.
Contenders release connections while waiting; owner writes reuse its connection.
Crash releases the lock but retains reservation and invocation intent. If recording
acceptance fails or cancellation interrupts confirmation, reconciliation recovers
the same attempt. Cancellation after a possible send records Uncertain using a
bounded independent cleanup token where possible; Pending+started is also a
recoverable conservative state if cleanup itself fails. Cancellation is cooperative;
adapter timeout/cancellation must not claim definitive non-acceptance without proof.

An adapter must enforce idempotency for the complete executable payload and retain
the key for business retention. Definitive failure is a strong receiver guarantee,
not a translation of HTTP errors or timeouts. Reconciliation is read-only. External
dispatch safety therefore depends on the receiver honoring this contract.

## Tools and authorization

`retrieve_manual_evidence` and `get_equipment_context` are read-only, available to
the Symptom Matcher. `validate_safety_requirements` is deterministic validation,
available to the Diagnostic & Safety Planner and uses a host-bound typed plan.
`dispatch_approved_work_order` is externally side-effecting and host-only; no
reasoning-agent role can call it. The trusted host may call all four subject to
resource authorization. Work Order Generator has no executor tools.

Registry schemas are closed; runtime checks reject unknown/extra/duplicate fields,
invalid values and roles. Approval, verification and dispatch are separate actions
on the required host authorization port. `actorId` is identity only: implementations
must bind it to authenticated host context and resource permissions. Missing
context fails closed. No production allow-all authorization is supplied.

Register `AddTrustedActions` after operational/knowledge registration with explicit
authorization, context accessor and external adapter. It routes existing agent
retrieval through the executor and wraps approval with action authorization while
preserving the existing consent/edited-scope policy. Trusted infrastructure/backend
ports are not exposed as model tools. Authorization and deterministic policy
implementations remain deployment responsibilities, not model-generated data.

Tracing records safe tool/outcome names through IRunTraceStore, without arguments,
prompts or raw exceptions. It is best-effort observation, independent of durable
dispatch events recording reservation, invocation, retries and reconciliation.

## Demonstration receiver and limits

`PostgresDispatchReceiver` creates a durable independently committed dispatch ticket
in a separate `dispatch_receiver` schema. Deploy it explicitly with
`DispatchReceiverSchema`; use a separate data source/pool (and optionally database)
from operational coordination. It is a meaningful local receiver, not an ERP
integration: acceptance means queued in this inbox, not physical execution.
Its UUID key and payload comparison reject duplicate/conflicting delivery. Tests
also use deterministic receivers to exercise rejection, timeout and crash windows.

No automatic retry daemon, ERP connector, authentication platform, force-unfreeze,
or scheduling UI is introduced. A future trusted host schedules reconciliation.
Unresolved acceptance intentionally remains frozen until receiver evidence is
available. Database access and raw persistence ports remain trusted internal
capabilities; direct SQL by an administrator is outside the application boundary.
