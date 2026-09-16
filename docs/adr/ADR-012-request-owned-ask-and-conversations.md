# ADR-012: Request-owned Ask streaming and durable conversation history

Status: Accepted for Issue #35 implementation

## Decision

Ask is a request-owned Application service consuming `IRetrievalService` and `ILlmProvider.StreamAsync`. It is not a specialist agent, MaintenanceRun, or durable ReasoningJob. The API authenticates the actor and authorizes the conversation's equipment/manual/revision before any retrieval. Infrastructure composes the PostgreSQL retrieval adapter through the existing Application port; Ask does not impersonate an agent tool context.

A conversation fixes equipment, document (Domain Manual identity), revision, owner and response culture. Each question is independently grounded against that revision; previous turns are display history, not implicit prompt memory. Hybrid is the default. A conservative lexical corroboration gate intersects Hybrid results with Keyword results and refuses an empty intersection. This is not a calibrated semantic-confidence score: paraphrases/cross-language queries without matching terms can be refused. No frozen FR-3 retrieval configuration or baseline is changed.

Only trusted retrieval entries become structured citations. DocumentId, ManualRevisionId, ChunkId, Locator and original Snippet are preserved exactly; no model-generated identifier is accepted as a source. Citations identify the supplied evidence, not a formal proof that every generated sentence is entailed. Question/evidence are untrusted user-message data; system instructions grant no tool execution, safety approval or dispatch authority. Answers are advisory and rendered as plain text.

The stream is `meta`, `evidence`, zero or more `delta`, `citation`, `completed`; refusal produces `completed` with `InsufficientEvidence` without calling the generator. Safe localized `error` ends a failed stream. Only public answer content is forwarded; no provider reasoning channels, wire payloads, keys or exception text. Provider deltas are awaited as produced, without completing an answer first. At most one provider item is processed per awaited HTTP write (no unbounded queue). Exact evidence excerpts have a combined 16,000-character budget (locators 512); oversized excerpts are not truncated into misleading citations. Answer cap: 32,000 UTF-16 characters; TopK: 1–10; question: 2,000 characters; default request timeout: 60 seconds (configured 1–60); write timeout: 10 seconds. Disconnect/navigation/cancel reaches the same linked token as retrieval and generation. No automatic reconnect or mutation retry.

This differs deliberately from ADR-008: disconnecting a T7 SSE observer stops observation only. Its Worker-owned reasoning job continues until an explicit durable cancellation request or terminal outcome.

## Persistence and failure boundaries

Operational migration 006 adds conversations and ask_turns. Owner predicates apply on every repository access, plus current equipment permissions at HTTP boundaries. Turn sequence is a database-generated ordering key. Conversation lists have bounded offset pages (created_at descending, ID tie-break); turn history uses an ascending sequence cursor. Refresh lists after concurrent insertions. Only one active turn per conversation is allowed: row locking plus a partial unique index arbitrate concurrent starts. Finalization checks ownership, active state and deadline and updates answer/citations/state in one transaction. No database transaction is held during generation.

Completed answers and refusal results survive API restart. Cancelled/failed turns retain the safe question and empty answer/citations, never a partial answer masquerading as completion. An interrupted process leaves Streaming until its 90-second deadline; inspection or the next start marks it Failed. It is not replayed or resumed. A finalization that committed before a broken final write remains Completed and can be inspected. Failed cleanup during a database outage falls back to expiry. The 90-second deadline fences stale finalizers and outlasts the maximum generation deadline. There is no background history retention/deletion policy in this slice.

History copies redact known configured credentials and common sensitive text using the existing boundary patterns. Original authorized source snippets remain unchanged for provenance; this is not an arbitrary-PII detection guarantee. Hosted outbound redaction remains active in the provider adapter. Uploaded source documents must follow deployment data governance.

## Ingestion and access

Authenticated `ingest` permission and equipment scope are required. HTTP streams raw text/PDF to the existing ManualIngestionService, with its extraction, cleaning, deterministic chunks, embedding/profile and atomic revision replacement. No second ingestion engine, OCR, arbitrary server path or file storage is introduced. File names are bounded labels only; slash/backslash/colon/control characters are rejected. Input is capped at 16,000,000 bytes; existing page/character limits remain. Retrying with the same document/revision replaces that revision; an empty/failed parse cannot delete existing knowledge. Per-attempt status remains available separately. New source associations require existing trusted-host equipment and cannot remap identities. Uploading does not add approved maintenance procedures.

Angular shows server-issued capabilities; Technician/Supervisor labels are presentation, never authorization. Credentials remain memory-only. After reload, reconnect and load the server-owned history. Domain D5 and Worker/T7 implementations are unchanged.
