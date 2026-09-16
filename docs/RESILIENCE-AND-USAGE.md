# FR-5 resilience and usage configuration

See [ADR-013](adr/ADR-013-resilience-and-usage-accounting.md) for policy and limitations.
Run the existing explicit host migration command before starting upgraded API/Worker binaries;
007 adds `operations.llm_usage` without changing existing work-order/job tables.

Host configuration (environment variables use double underscores):

| Key under `Reasoning` | Default | Valid range |
| --- | --- | --- |
| ModelTurns | 4 | 1–16 per agent |
| ToolCalls | 4 | 1–16 per agent |
| TopK | 5 | 1–20 |
| AgentTimeoutSeconds | 60 | 1–300 |
| Attempts | 2 | 1–3 total per read-only call |
| RetryDelayMilliseconds | 250 | 0–5000; exponential |
| FallbackTimeoutSeconds | 15 | 1–60 |

The agent deadline includes retries/backoff. Fallback has no additional provider retry loop.
Cancellation propagates; cleanup may take up to five seconds. Validation/auth/safety errors never
become an advisory fallback. An advisory answer cannot be submitted as an approved work order.

## Optional estimated pricing

`Usage:Prices` is an array. This **fictional example** explains configuration, not a commercial quote:

```json
{"Usage":{"Prices":[{"Provider":"OpenAi","Model":"your-configured-model",
"Currency":"USD","Version":"deployment-reviewed-v1","EffectiveFrom":"2026-01-01T00:00:00Z",
"TokensPerUnit":1000000,"InputRate":1.0,"OutputRate":2.0}]}}
```

Replace rates with reviewed deployment values. Keep old effective rules when adding a new version.
No configured rule means unknown cost; local/Demo calls always have null hosted cost. Model aliases
must be reviewed when provider configuration changes. Existing `EmbeddingProfile` semantics do not change.

## Inspect usage

With the same authenticated user's credential, query `/api/usage?correlationId=<UUID>&limit=50` or
`?runId=<UUID>&provider=OpenAi&from=<ISO8601>&until=<ISO8601>&offset=0&limit=50`.
`from` is inclusive, `until` exclusive; limit 1–100, offset 0–10000. Foreign ownership/equipment
returns no rows. Records include physical call IDs, purpose, model, timestamps, final state, nullable
usage/cost/version. Summary separates unknown tokens, unpriced hosted calls and non-hosted calls.
A persisted Started row after restart is an uncertain call, not an automatic retry instruction.
Do not publish real credentials when collecting proof.

## Local proof

Focused regression tests: `ResilienceTests`, `UsageAccountingTests`, `UsageStoreTests`, and
`ProductHttpTests.RealHttpTransientExhaustionProducesGroundedAdvisoryFallbackAndCorrelatedUsage`.
The latter uses real HTTP + PostgreSQL + deterministic test adapter to exhaust two attempts, run the
existing grounded Ask path, preserve exact manual/revision citations and query correlated ledger rows.
It does not claim a live commercial-provider call. Set `RAG_TEST_POSTGRES` to an isolated local
pgvector server for integration tests. Existing Demo product/T7 smoke scripts remain the regression
entry points. Inspect the diagnosis fallback notice and source details in either Arabic or English.

For an opt-in live fault demonstration, start the existing Development Demo executable with
`--demo --transient-reasoning` (same loopback `DEMO_POSTGRES` setup as DEMO-GUIDE), then run
`node tools/fr5-smoke.mjs`. Restart the same host/database and run
`node tools/fr5-smoke.mjs --verify-restart`. Proof goes to ignored `artifacts/fr5-live-proof.json`.
The fault switch exists only in the separate synthetic Demo executable, never production API
configuration. Restart without the switch for the normal approval/dispatch and product demos.

Safe trace responses expose `stepId` and `parentStepId`, allowing usage `context.stepId` to be
joined to `/api/traces/{executionId}` without exposing prompts or internal reasoning.
