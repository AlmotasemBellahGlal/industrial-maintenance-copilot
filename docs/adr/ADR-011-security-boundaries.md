# ADR-011: Security controls at HTTP and provider boundaries

Status: Accepted implementation decision, Issue #33.

Reuse equipment-scoped host permissions, trusted tool execution, D5 state invariants and T7 transactions. Add authenticated actor-wide HTTP mutation admission with ASP.NET Core rate limiting; do not partition by caller-supplied resource IDs or forwarded IP headers. Production distributed/edge limiting remains a deployment concern.

Treat the hosted provider as an external recipient. Redact outbound copies with bounded pattern matching and preserve JSON structure and opaque references. Do not rewrite authoritative evidence or executable Domain data. Local provider text remains local/unredacted. Include hosted-redaction-v1 in index binding so old hosted vectors cannot silently mix with new preprocessing. Affected operators must create a new profile and reindex; no automatic migration is performed.

Keep fixed agent tool permissions, untrusted evidence role separation and bounded output parsing as structural defenses. Prompt instructions alone cannot authorize actions. Two independent demo credentials demonstrate actual permission differences.

Pin and checksum-verify full-history Gitleaks; scan transitive NuGet/npm dependencies and fail on High/Critical or scanner failure. Patch OpenApi within its compatible 2.x line instead of warning suppression. No custom authentication framework, universal PII claim or semantic injection immunity is introduced. SECURITY.md records evidence and residual risk.
