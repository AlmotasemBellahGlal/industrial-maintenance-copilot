# Public demo overlay (UNTESTED, prep only)

Scope: TLS + closed ports + a few proxy rules around the existing synthetic demo
stack. No C# change. The container demo still runs in Development mode
(`tools/IndustrialCopilot.Demo/ContainerDemo.cs:24-27` requires it).

What this does NOT solve (verified in source):
- Conversations are actor-scoped (`ProductEndpoints.cs:38-47`), so one shared token
  means all visitors share one conversation list.
- Jobs, runs, work orders and traces are equipment-scoped, not owner-scoped
  (`ReasoningJobEndpoints.cs:13-14`, `MaintenanceEndpoints.cs:33-45`): any holder of a
  token for the demo equipment can read any run/job by ID.
- Rate limit is 30 POST/min per actor (`ApiSecurity.cs`), shared by every holder of a token.
- No cap on stored runs/jobs was found.
Do not publish a token publicly. Give each reviewer a separate Technician token
only after a threat review of how tokens are issued and revoked.

Validation checklist before any public use: see the PR description.

## Application changes (validated with dotnet 10 + Postgres/pgvector 16 where noted)
- Jobs are owner-scoped (`ReasoningJobEndpoints.Read`; approvers excepted). Snapshot carries `Actor`.
- Runs, work orders, evidence, dispatch attempts and traces are owner-scoped via a new
  `operations.run_owners` table (migration 008, `RunOwnership`/`IRunOwnership`), written in
  `MaintenanceEndpoints.Prepare` (used by /runs, /runs/stream and /jobs). Non-approvers must be the
  owner; runs with no recorded owner (created before the migration) are approver-only.
  Approvers (permission `approve`) can read everything, as before. Traces already had an
  equipment check inside the store (`HostAccess.CanAccessTraceAsync`); ownership is added on top.
- Active-job cap: 3 per actor, 25 total, HTTP 429. A global advisory lock serializes the check
  (always taken after the per-actor lock), so concurrent submissions cannot overshoot.
- Angular: synthetic-demo notice (English + Arabic).
Not changed: Development mode, 30 POST/min per actor, no demo reset, usage is already
filtered by actor and equipment (`PostgresLlmUsageStore.QueryAsync`), TLS overlay is untested.
