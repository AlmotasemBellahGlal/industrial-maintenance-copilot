# FR-1 corpus and ingestion

## Scope and safe source

The assessment corpus is **entirely synthetic**. It is repository-generated fictional training material, not a real manufacturer's manual and not authorization to maintain real equipment. No personal, confidential, proprietary or downloaded manufacturer content is used.

`tools/IndustrialCopilot.Corpus` generates **31 distinct document identities: 30 PDFs with 5 real pages each (150 pages), plus one UTF-8 text document**. The text document contributes no pages. Ten equipment families cover pump, motor, compressor, conveyor, bearing, valve, gearbox, fan, hydraulic unit and cooling loop. Each has fault-isolation, preventive-inspection and return-to-service variants. Stable identities and fixed text make regenerated chunk IDs reproducible; incidental PDF container metadata is not an ingestion identity.

The generator validates the actual PDF page objects and selectable text, rejects duplicate document IDs and fails below 30 documents or 150 PDF pages. Tests deliberately remove a document and separately leave 30 documents with only 145 pages. This is a small educational corpus with deliberate procedural repetition, not an evaluation benchmark or certified maintenance handbook.

## Reproduce locally

Use .NET 10 and the existing Docker/PostgreSQL setup. Do not point the corpus tool at a production database. No paid model key, Ollama install or model download is required. The following creates only a PostgreSQL dependency, **not a full-stack Compose deployment**:

```powershell
docker run -d --name industrial-copilot-fr1-pg -e POSTGRES_PASSWORD=isolated_fr1_only -e POSTGRES_DB=maintenance_corpus -p 127.0.0.1:15432:5432 pgvector/pgvector:pg16
# Wait until initialization has fully finished and pg_isready reports readiness.
docker exec industrial-copilot-fr1-pg pg_isready -U postgres -d maintenance_corpus
$env:CORPUS_POSTGRES='Host=127.0.0.1;Port=15432;Database=maintenance_corpus;Username=postgres;Password=isolated_fr1_only'
dotnet run --project tools/IndustrialCopilot.Corpus -- --write
dotnet run --no-build --project tools/IndustrialCopilot.Corpus -- --ingest
dotnet run --no-build --project tools/IndustrialCopilot.Corpus -- --ingest
dotnet run --no-build --project tools/IndustrialCopilot.Corpus -- --status
```

Use an existing ready container instead of creating another when already provisioned. Port 15432 avoids the reserved 55432 range observed on the development host. Generated files/manifest live under ignored `artifacts/corpus`; generated binaries are not committed. `--write` regenerates these known artifact filenames only. Without flags, the tool generates and validates counts in memory. `--ingest` explicitly applies knowledge migrations, ingests all documents, checks Completed status, and demonstrates Keyword/Dense/Hybrid with revision-filtered exact citations. It prints document/page/chunk totals and evidence JSON. `--status` reads the latest 100 attempts per corpus document/revision.

The smoke uses the same `ManualIngestionService`, PostgreSQL index and processing adapters as the host. Only embeddings are replaced by deterministic lexical test vectors (32 dimensions, profile `assessment-corpus-v1`, binding/model `synthetic-lexical-v1`). All formats share this space. This is reproducibility evidence, not a semantic retrieval-quality claim.

## Formats and stages

1. **Extract:** UTF-8 `text/plain` and `application/pdf` have real adapters. Text is strictly decoded, from the caller's current stream position. PDF uses PdfPig text extraction on actual pages; no OCR, attachment processing, URL fetching or executable content execution. Streams need not seek and remain caller-owned.
2. **Clean:** remove a leading BOM and reject invalid control characters. Preserve line endings, tabs, internal whitespace and source wording; no lossy whitespace collapse, dehyphenation or inferred text. Empty text cannot delete knowledge.
3. **Chunk:** the existing overlapping Unicode-scalar windows remain `text-v1`, including their original IDs/line locators. PDF windows use the same algorithm independently within each page; page-specific identity prevents cross-page collisions. The default is 1200 scalars with 200 overlap. No chunk crosses a PDF page.
4. **Embed:** the existing ILlmProvider embedding port and batch validation enforce count, nonzero finite dimensions, expected model and configured embedding space.
5. **Index:** the existing atomic revision replacement transaction publishes all chunks or none. For reported ingestion, it also marks the attempt Completed in that transaction.

Application owns stage interfaces and composition; Infrastructure owns parsing, cleaning/chunking mechanics and PostgreSQL. The original `TextDocumentProcessor` remains a compatibility facade over shared stages, with no second copy of its algorithm.

## Provenance

DocumentId is Domain Manual.Id; ManualRevisionId is the immutable revision identity. Adapters do not reconstruct aggregates. Optional typed source metadata contains title, opaque source label and positive revision number. New assessment requests populate it. Existing requests remain compatible; missing metadata is not fabricated.

Chunks persist that metadata, exact original document/revision IDs, stable ChunkId, locator, content, optional physical PDF page and an explicit section heading when exactly one `Section ...:` heading exists in the source segment. Multiple/absent headings are not guessed. Text uses original logical source line/scalar positions. PDF locators identify the actual page plus **extracted-text** line/scalar positions, not physical PDF coordinates. Clause labels remain in source text. RetrievalResult and GroundedEvidence retain their existing document/revision/chunk/locator/snippet meaning; scores are not added to agent evidence.

## Replacement and concurrency

Equivalent re-ingestion produces the same chunk IDs and one logical indexed revision, with a separate audit attempt per invocation. Changed content under the same document/revision intentionally replaces that revision; unchanged windows retain IDs, changed windows receive new IDs. This supports operator corrections, not mutation of Domain ManualRevision identity. A genuinely new published version should use a new ManualRevisionId and revision number; older revision indexes remain independently addressable.

Concurrent requests perform independent processing. PostgreSQL serializes the revision replacement; **the last successful commit wins as one whole batch**, never a union. Invocation order is not a priority/version ordering guarantee. No transaction or revision lock is held across model calls. Profile/binding/dimension checks and repeatable-read retrieval remain unchanged. Parser, cleaning, embedding, cancellation or index failure does not erase the previous valid revision. Empty extraction records EmptyDocument and does not replace anything.

## Durable reporting and interruption

Each invocation has its own attempt ID, document/revision/metadata, Processing/Completed/Failed state, current stage, safe failure enum, timestamps and available page/chunk counts. Failures are categorized as UnsupportedFormat, InvalidDocument, InputTooLarge, EmptyDocument, Cancelled or StageFailed. Raw exception messages, provider responses and credentials are not persisted.

A dedicated **unpooled** PostgreSQL session marks an attempt's liveness (backend PID plus backend start time). Inspection derives **Interrupted** for unfinished attempts after PostgreSQL observes that session ending. This survives service recreation and cannot confuse a reused PID with the original session. Network failure detection is bounded by PostgreSQL/TCP behavior, not instantaneous; no timeout is falsely called a confirmed failure. There is no automatic ingestion replay/queue in this issue. Operators inspect and safely retry interrupted attempts. A later successful attempt cannot be marked failed by an earlier attempt.

Completion and index content commit together. The transaction rejects an inactive/foreign attempt; rollback preserves previous content. A late cancellation/error cannot overwrite an already committed Completed status. When storage itself is unavailable, reporting may be unavailable too; once available, ended sessions expose Interrupted rather than claiming success. History reads are bounded to the latest 100 attempts per revision; retention/pruning is not implemented.

## Existing operator command

The ingestion surface remains an operator-only command, not an unauthenticated HTTP file-upload endpoint. It retains the configured procedure/manual-revision allowlist:

```powershell
dotnet run --project src/IndustrialCopilot.Api -- --ingest '<manual-guid>' '<revision-guid>' '<local .txt or .pdf path>' --ingest-metadata 'Manual title' 'operator-owned-source-label' 1
dotnet run --project src/IndustrialCopilot.Api -- --ingestion-status '<manual-guid>' '<revision-guid>'
```

Metadata is optional for compatibility, but should be supplied for new sources. Source labels are never opened or fetched. File paths are local trusted-operator inputs; no untrusted HTTP path is accepted. Missing/unreadable files fail before opening an ingestion attempt; the command reports a safe failure and nonzero exit code. Stage failures after opening a source are inspectable by document/revision. Empty extraction is reported as a nonzero command result and never deletes existing knowledge. Existing authentication/approval/verification/dispatch boundaries are unchanged. Ingesting assessment material does **not** register it as a trusted executable safety procedure.

## Limits and verification

Inputs are capped at 16,000,000 bytes; extracted text at 4,000,000 characters, PDF at 500 pages. Unsupported types, malformed/encrypted unsupported PDFs, and PDF pages without selectable text fail closed rather than publishing partial content. Image-only scans require a future OCR decision. PDF layout extraction is best-effort for complex columns/tables; this issue does not promise general document-layout fidelity or a sandbox against every parser resource attack. Cancellation is checked during reading and between page/chunk operations; a synchronous parser call is not preempted mid-call.

Run `dotnet build`, then `dotnet test` with `RAG_TEST_POSTGRES` pointing to an isolated pgvector server. Integration tests create/drop uniquely named test databases. Tests cover both formats, conservative cleaning, deterministic IDs, stage failures, metadata/pages, corpus thresholds, concurrent retries, atomic replacement, interruption detection and original Keyword/Dense/Hybrid/profile/revision regressions. CI additionally ingests the full corpus twice and runs the original T7 and bilingual D5 smoke. Frontend files are unchanged.
