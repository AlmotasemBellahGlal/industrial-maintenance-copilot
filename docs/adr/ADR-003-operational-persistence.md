# ADR-003: Operational snapshots and audited PostgreSQL writes

- Status: Accepted
- Scope: Issue #15

## Decision

Reuse PostgreSQL/Npgsql in a separate `operations` schema and dedicated keyed
connection pool. Domain and Application remain database independent. No EF or
new database packages are required.

Domain-owned Restore factories reconstruct valid stored WorkOrder/MaintenanceRun
state without replay, revision increments, new decisions, reflection, or setters
exposed to Infrastructure. Restored decisions and verification are observations
from trusted persistence, never credentials or proof of authorization. Factories
validate lifecycle, assessment, history ordering and dispatch consistency.
Restoration is not an API/agent command.

Work orders use a current-head row pointing to an immutable relational snapshot.
Each successful save retains content, ordered actions, authoritative requirements,
verification and full decision history together. Prior snapshots retain the exact
scope visible before later edits. A deferred foreign key ensures every committed
head has a complete snapshot. Equipment/manual/revision tables hold operational
identity relationships only, not duplicate manual documents or RAG chunks.
Run associations must match equipment. An imported aggregate can preserve its
historical decisions but cannot invent missing historical executable scopes;
full scope audit retention starts with its first stored snapshot.

A fresh UUID is the opaque concurrency token for every persisted mutation,
including verification and submission without scope revision changes.
Writers lock the current head and compare the supplied token; approval writes also
compare Domain Revision. New-record races use primary-key conflict arbitration.
Head replacement and all snapshot children share one transaction. Failed writes
roll back; uncertain commit cancellation requires rereading before retry.
Approval retries with stale targets return Conflict and never add a decision.
Run snapshots use their own UUID compare-and-swap token.

Approval service implementations call Domain behavior only after a required
OperationalAccessPolicy supplied by the trusted host authorizes the operation
and validates exact edit scope/consent. No allow-all policy is registered.
The same policy controls trace access through trusted ambient host context.
No dispatch operation is exposed by the approval service. Raw workflow storage
is a trusted internal composition capability, not an authorization facade.

Trace trees are document-shaped JSONB with indexed execution/correlation/run IDs
and an explicit version column. Deserialization invokes validated constructors.
ExpectedVersion is atomic; equivalent retries compare structured values, not
array references or JSON text. Identity/parent/kind/start observations cannot
disappear; terminal status/end/error and known usage/cost cannot be overwritten.
Missing usage/cost can arrive later. Totals remain derived, never incremented.
Trace run IDs are observations and need not point to a locally stored run.

DateTimeOffset audit values use invariant round-trip text to preserve original
offsets and 100ns precision (PostgreSQL timestamptz normalizes offsets and has
microsecond precision). These fields are not used as concurrency tokens.

## Consequences

Snapshot children use more space than in-place updates but simplify atomic audit
retention and reliable reads. Retention/redaction policy is future deployment
work. PostgreSQL privileges must restrict snapshot UPDATE/DELETE and raw table
access to trusted administration. Event sourcing and ORM private-field mapping
were rejected: they add complexity or bypass the Domain restoration boundary.

## Configuration and verification

Supply `ConnectionStrings__Operations` through secret configuration; it may name
the same PostgreSQL deployment as knowledge. Register with
`AddOperationalPersistence(configuration, hostPolicy)`. Run
`OperationalSchema.ApplyAsync` explicitly with migration privileges before use;
serving credentials need no DDL rights. No migration runs on an approval request.
No sensitive payloads, SQL errors, credentials or vectors are logged.

Integration tests reuse the isolated pgvector CI service and create a dedicated
random test database. Without RAG_TEST_POSTGRES, database tests explicitly skip;
ordinary unit tests require no running database or provider.
