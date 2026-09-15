# ADR-009: staged manual ingestion and per-attempt reporting

Status: Accepted for FR-1 / Issue #29.

The existing IDocumentProcessor and atomic revision index are retained. Application composes explicit Extract, Clean and Chunk ports, then the existing embedding and index ports. Infrastructure adds selectable-text PDF extraction with [PdfPig](https://github.com/UglyToad/PdfPig), alongside strict UTF-8. PdfPig is confined to Infrastructure; no Domain or Application dependency changes.

Cleaning preserves source wording/line offsets. Text-v1 identity remains stable. PDF identities include page identity and use the same window algorithm. Optional typed source metadata is additive, and existing GroundedEvidence is unchanged. All formats use one configured embedding profile.

Each operator invocation records an independent attempt. Do not maintain a single mutable document status that concurrent attempts can overwrite. Index completion and the revision replacement share one database transaction. A failed replacement cannot claim completion or destroy the prior index.

An unpooled PostgreSQL session identifies the lifetime of processing. Its backend PID/start pair allows status reads to derive Interrupted after connection/process loss. This is intentionally not an ingestion scheduler or T7 reasoning-job reuse: ingestion is an explicit operator command, with retry via idempotent replacement. Runtime connection configuration is injected from the host, never reconstructed from Npgsql's redacted data-source ConnectionString.

Concurrent replacements are last-successful-commit-wins, serialized at the existing revision row. No distributed lock, model call inside a database transaction, separate vector space per format, generic job framework or raw exception persistence is introduced. The reporting connection adds one database session per active ingestion; operator concurrency must respect database capacity. Lost sessions cannot publish reported completion. Detection of network partitions waits for PostgreSQL's connection liveness observation; do not describe it as immediate failover.

The corpus generator owns deterministic synthetic text, identities and actual 150-page PDF structure. Generated files are ignored artifacts rather than committed binaries. Extraction tests inspect actual PDF pages. This satisfies the corpus threshold without claiming real manufacturer origin or production safety certification.

See [CORPUS-INGESTION](../CORPUS-INGESTION.md) for replacement semantics, limits, commands and evidence. Safety policies and supervisor/dispatch workflows remain independent of ingested advisory material.
