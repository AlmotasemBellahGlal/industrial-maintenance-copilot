# ADR-013: Bounded reasoning resilience and physical LLM usage accounting

- Status: Accepted for implementation (Issue #37)
- Extends ADR-004, ADR-008 and ADR-012; preserves their safety, lease and ownership boundaries.

## Audit and decision

The existing **Sequential Pipeline** already supplied three fixed agents, model/tool budgets,
per-agent cancellation deadlines, inspectable run/trace IDs, and audited human decisions.
Gaps were host-configured limits, transient read-only retries, advisory grounded degradation,
and a durable ledger covering all physical provider operations. Existing provider fallback and
T7 interrupted-attempt recovery are reused, not multiplied.

Application classifies dependency failures as terminal, transient or timeout. Each agent has one
shared deadline including calls and backoff. Only fixed provider calls and the allow-listed
read-only evidence tool retry transient failures, at most `Attempts` total physical attempts.
Backoff is `RetryDelayMilliseconds * 2^(failedAttempt-1)`; no infinite loop. Timeout goes directly
to fallback rather than restarting an expired step. Terminal/auth/schema/validation/safety failures
and caller cancellation do not retry or trigger fallback. Unknown failures are terminal.
The managed-call scope suppresses Infrastructure's optional primary-to-secondary fallback.
Outside managed reasoning, existing one-shot provider fallback remains unchanged. Embeddings
never switch embedding space. T7 does not replay a completed technical failure; its separate
bounded lease-recovery policy applies to interrupted attempts. Dispatch is never in this retry loop.

Eligible exhausted transient or agent/provider timeout failures may enter **one** plain RAG call
under a separate deadline. It reuses AskService's Hybrid + keyword evidence gate, exact revision
and citation catalog, narrative language and cancellation. A single trusted candidate is required;
ambiguous selection refuses. Safety-policy timeout is deliberately ineligible. No work-order,
approval, verification or dispatch capability is available to this fallback. The maintenance run
remains Failed: `Degraded` is an advisory answer, `DegradedRefused` means insufficient evidence,
neither is successful maintenance execution. API/SSE contain the reason and exact retrieved
citations. Retry/fallback events and failed-attempt trace steps are inspectable. Result transport is
bounded; an oversized answer/citation set refuses rather than truncating provenance.

## Usage ledger

Application defines usage, pricing and store contracts. Infrastructure decorates each physical
adapter **below** provider selection, including completion, tool completion, streaming and embedding.
Host middleware and durable-job execution supply trusted actor/correlation/equipment; orchestration
adds run/execution/agent/step and purpose. No model-supplied actor or IDs become authorization.

Migration `operations/007_llm_usage.sql` creates the append/finalize ledger. A unique call ID is
persisted before generation; finalization is compare-and-set and idempotent. A crash may leave
Started, meaning unknown completion, not zero consumption. Successful/failed/cancelled/incomplete
are separate states. Completion cleanup uses an independent bounded five-second token; if cleanup
fails during cancellation, cancellation still propagates and the Started row remains honest.
Usage persistence failure otherwise fails closed. There is no hidden request replay to repair it.

Tokens are nullable. Missing provider usage is not invented; malformed supplied usage remains an
error. Streaming records final reported usage when available; partial cancellation/disposal does
not estimate token counts. Embedding input usage is mapped when supplied. Configured model identity
is recorded (an alias may differ from provider response metadata); rates must match that configured
identity. Demo is Synthetic with unknown tokens; Ollama is Local. Neither has hosted monetary cost.

Pricing is external, opt-in and versioned: provider/model/currency/version/effective-from,
TokensPerUnit, InputRate, OutputRate. Latest effective rule at call start wins; duplicate effective
rules fail configuration. Decimal estimate = (prompt * input rate + completion * output rate) / unit.
It is an estimate, not an invoice. Missing tokens or price => null cost, never fabricated zero.
No commercial prices are bundled. Usage rows contain no prompt, completion, raw response, tool
arguments, authentication headers or hidden reasoning.

`GET /api/usage` requires authenticated read permission and always filters by current actor AND
permitted equipment before paging/aggregation. No actor override exists. Correlation/run/provider/
date filters and bounded limit/offset are available. Summary covers the entire filtered set, not just
the page, in one repeatable-read transaction. Known token sums remain null if none exist; unknown
counts are separate; known costs group by currency. Startup/system calls lacking equipment remain
persisted but absent from user-scoped queries. This endpoint is not an administrator billing console.

## Limitations

Natural-language grounding is not semantic proof. No production SLA/circuit breaker or distributed
retry budget is claimed. Safe T7 replay may incur additional genuine calls, each accounted separately.
A crash or missing final usage can leave costs unknown. Retention/administrative billing and provider
invoice reconciliation are future work. Hosted wire mapping is contract-tested; live paid models
are not required or claimed. Frozen FR-3 data and all Domain approval/dispatch invariants are unchanged.
