# ADR-014: Isolated Docker Compose evaluator packaging

Status: Accepted for implementation, Issue #39.

The existing product has working migrations, ingestion, host authentication and
durable workers, but host-only setup depended on SDKs and local configuration.
Package those components through multi-stage digest-pinned images and an opt-in
Development/container entry point in the existing Demo tool. Production API and
Worker targets retain their existing registration and fail-closed configuration.

Compose generates random credentials/internal TLS into named volumes, starts
PostgreSQL before migrations and seed, then API/Worker and same-origin Nginx.
Only the loopback web port is published. Internal verified HTTPS preserves the
API's non-loopback TLS requirement instead of weakening it for Docker. A shared
demo image avoids repeated publishing while production runtime targets remain
available independently. The frontend receives no private secret volume.

Corpus initialization calls the existing pipeline. Evaluation uses its own
database namespace and preserves the evaluator's existing isolation guard.
Data and secrets survive ordinary shutdown. Smoke tools run as optional containers
without a Docker socket; the operator performs explicit Worker crash/restart.

This is an evaluator deployment, not a production cloud installation. External
TLS/identity, backups, certificate rotation, availability and real provider
operation remain deployment responsibilities. No state-machine, safety, RAG,
retry, cancellation or account semantics are changed by packaging.
