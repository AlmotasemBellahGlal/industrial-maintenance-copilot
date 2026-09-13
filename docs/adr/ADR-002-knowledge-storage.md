# ADR-002: PostgreSQL and pgvector for manual knowledge

Status: Accepted for Issue #13

The initial knowledge pipeline stores chunk text, exact manual/revision/chunk
provenance, lexical search data, and vectors together in PostgreSQL. Npgsql is
an Infrastructure-only dependency; existing Application ports remain unchanged.
The small ingestion use case in Application composes those ports without
depending on any parser, embedding provider, or database implementation.

## Decision

Use PostgreSQL 17 with pgvector, a GIN index over the `simple` full-text
configuration, and exact cosine nearest-neighbor retrieval. Keyword queries use
`plainto_tsquery` and `ts_rank_cd`. Hybrid queries fuse the configured candidate
lists with reciprocal-rank fusion, equal weights and k=60. Ties use document,
revision, then chunk identity. Exact vector scans are deliberate for the initial
corpus; approximate HNSW indexes can be added later with measured recall and
profile-specific dimensions. No semantic score is invented from keyword scores.

One transaction locks a stable revision row, deletes its obsolete chunks and
installs the complete replacement. Equivalent retries do not append duplicates;
concurrent writers cannot produce a union. Repeatable-read retrieval sees a
single committed snapshot across provenance checks and both hybrid rankings.

An application-owned EmbeddingProfile is registered with dimensions and a hash
of selected provider, endpoint, model, and deployment-owned immutable embedding
revision. Reusing a profile with a different binding/dimension fails. Query
embeddings must also report the configured model and valid nonzero vectors.
Embeddings never fall back. Operators must pin model weights/preprocessing and
change profile/reindex when they change; dimensions or mutable model aliases
cannot prove equivalence. The database does not infer a vector's origin.

The first processor accepts strict UTF-8 text/plain from the current stream
position and preserves source line/scalar locators. SHA-256-derived chunk UUIDs
include revision, offsets, content, and chunk configuration. PDF/OCR is an
explicit upstream extraction concern, not silently treated as text.

## Alternatives and consequences

A separate vector database would require coordinating text/provenance and
replacement atomicity across stores. An in-memory index would not be durable.
EF Core adds no value to this small SQL-focused adapter. PostgreSQL plus pgvector
provides the required lexical/vector capabilities and transactions in one store.

`simple` search preserves industrial terms without language-specific stemming;
it is not a multilingual linguistic analyzer. Exact vector search costs a scan
of the selected profile. Dense results require configured minimum cosine
similarity (default zero); hybrid scores are rank-fusion scores, not probabilities.
Raw text has no PDF page metadata, so its locators do not claim page numbers.

Schema migration is explicit and requires extension/DDL privileges; serving
requests do not migrate. Real SQL tests run against an isolated pgvector service
in CI; local unit tests need no database or model process.
