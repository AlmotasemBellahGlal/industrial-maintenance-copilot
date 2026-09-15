# ADR-010: Measure RAG through existing paths with a frozen deterministic baseline

Status: Accepted for FR-3 implementation, Issue #31.

The repository needs repeatable retrieval, groundedness and refusal measurements without paid credentials. A new QA implementation or case-specific fake provider could create misleading perfect scores.

Use a standalone evaluation composition root that invokes existing ingestion, PostgreSQL retrieval and Application SymptomMatcherAgent with the unchanged demo provider. Keep golden labels outside execution inputs. Freeze a versioned dataset before observing results, fingerprint canonical LF text, and add only isolated synthetic adversarial/revision fixtures. Reuse existing embeddings rather than claim semantic-model quality.

Report mode-specific retrieval probes separately from the agent's Hybrid execution. Report citation/fact groundedness explicitly as a proxy. Keep system errors separate, false refusals visible and unsupported clarification expectations as failures. CI gates harness correctness and reproducibility, not an invented quality threshold. Require an isolated database and reject foreign revisions.

Consequences: no Application/Domain/provider changes or paid dependency. Results characterize a deliberately limited deterministic path; low scores are retained. This is not full orchestrator evaluation, semantic entailment certification or a security guarantee. A generic QA endpoint, new provider and learned judge are rejected for this focused slice and may be considered separately.
