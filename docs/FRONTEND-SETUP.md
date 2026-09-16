# Angular operations workspace

The browser is an **untrusted client** of the Issue #21 API. It displays proposals and safe execution observations; it cannot establish safety, authenticate an actor, or authorize dispatch independently.

## Tooling and design

Issue #25 adds immediate English/Arabic switching, persisted language preference,
RTL layout and centralized Accept-Language propagation for REST and SSE. Credentials
remain memory-only. Source citations and executable scope are never translated.
See [Arabic / English Demo](DEMO-GUIDE.md#arabic--english-demo) for the language boundary,
real-backend browser test and synthetic demo startup.

- Angular 22 standalone components, strict TypeScript/templates, lazy Router routes, typed Reactive Forms, HttpClient and local signals. No NgRx.
- Node 26.x and npm 11.x; see [Angular compatibility](https://angular.dev/reference/versions).
- UI UX Pro Max CLI 2.15.0, installed by the human with `uipro init --ai codex`. Its Python design-system workflow generated [MASTER.md](../design-system/industrial-maintenance-copilot/MASTER.md). Local `.agents/` installation is ignored; reviewed design output is tracked.
- Fira fonts are bundled through fontsource; no runtime CDN requests. No decorative charts, invented metrics or AI illustrations.

## Start backend and browser

First follow [HOST-SETUP.md](HOST-SETUP.md) for PostgreSQL/pgvector, reviewed procedures, explicit migrations, ingestion, provider selection and credentials. Set private `MAINTENANCE_CONFIG` in the API/Worker shells; do not copy credentials into the repository.

```powershell
# API terminal: loopback HTTP is permitted only for Development / Testing.
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet run --project src/IndustrialCopilot.Api --no-launch-profile -- --urls http://127.0.0.1:5000

# Worker terminal with configured operational databases.
dotnet run --project src/IndustrialCopilot.Worker

# Browser terminal
cd src/IndustrialCopilot.Web
npm ci
npm start
```

Open `http://localhost:4200`. `proxy.conf.json` forwards `/api/**` and `/health/**` to loopback port 5000. Change that target if your API binding differs. The browser uses centralized same-origin `/api` in `core/api.ts`; bearer credentials are not attached to arbitrary third-party URLs. No CORS change is necessary.

In **Connection**, paste a host-issued bearer credential. It stays in memory. Reloading clears the credential and session activity. **Credential configured** does not claim authentication: each server request authenticates and checks equipment-scoped authorization. The API has no who-am-I/permissions endpoint; the UI does not fabricate one. Readiness checks schemas, not identity or LLM availability.

## Production deployment

Run `npm run build`. Serve `dist/industrial-copilot-web/browser` over HTTPS with SPA fallback to `index.html` for frontend routes. Proxy `/api/` and `/health/` to ASP.NET **before** SPA fallback. Use a trusted HTTPS upstream to the API in Production: the host rejects plain HTTP and does not trust arbitrary forwarded-protocol headers. Do not expose a Development API to bypass TLS.

Keep credentials outside build-time environment files and web assets. For `/api/runs/stream`, disable proxy buffering/caching and allow the backend workflow timeout. Use a restrictive CSP appropriate to Angular assets and same-origin connections. Do not log Authorization headers or sensitive request bodies. Production ingress and external identity providers are deployment concerns, not implemented by this client.

## Demonstration path

1. Configure dependencies, reviewed procedures and known equipment/manual revision; explicitly migrate and ingest the manual as documented in HOST-SETUP.
2. Start API, Worker and Angular. Configure a credential with read/start and the separate approve/verify/dispatch permissions required by the demo roles.
3. **New diagnosis**: enter a supported equipment UUID and symptom. No equipment listing endpoint exists; the deployment supplies supported IDs.
4. Observe actual safe SSE stages. No EvidenceRetrieved event is invented when the API does not emit one. Inspect traces for exposed agent/tool metadata.
5. Open the work order and citations (manual, revision, chunk, locator, literal snippet). Original citations are labelled historical after editing.
6. Approve or Reject the displayed revision using contextual confirmation. No rejection reason is collected because the API does not accept one.
7. For **Edit & approve**, edit the allowed scope, assess it, inspect read-only requirements, and confirm that exact snapshot. Any edit invalidates the preview. The echo is comparison-only; the server recomputes safety. Old verification never transfers.
8. For a physical check actually performed, select the prerequisite and record evidence. Checked means satisfied; unchecked records unsatisfied. Confirmation and host response precede any verified badge change.
9. Review dispatch confirmation. The host rechecks current approval/revision/safety. Pending/Uncertain is not successful delivery or definitive failure.
10. Follow the existing attempt to Dispatch; refresh performs GET only. The Worker reconciles using the original durable key. Do not repeat delivery.
11. Inspect runs and safe traces. The endpoint exposes stage metadata/error codes, not token/cost accounting, prompts or hidden reasoning.

## Recovery and limits

- Dashboard/lookup retain at most 30 observed records, labelled potentially stale. They are not global listings or metrics.
- SSE uses authenticated fetch/ReadableStream POST. It normalizes PascalCase envelopes and numeric progress; REST remains camelCase.
- No automatic reconnect/replay: start creates new server IDs. Stop/disconnect/navigation requests cancellation, but does not prove durable execution stopped. Inspect the known run. If no ID arrived, use operator correlation records before starting again.
- Progress deduplicates and retains at most 100 observations. Frames are bounded to 64 KiB. Truncated streams mean unknown outcome, not success.
- 401 requests credentials; 403 explains permissions; 409 pauses actions until reload/review; 422 explains host rejection. Dependency/timeout errors require inspection before consequential retry. Raw errors are never rendered.
- No deletion, retry-dispatch, cancellation endpoint, global inventory, live metrics or backend orchestration recovery is invented.

## Validation

```sh
# Frontend directory
npm ci
npm run typecheck
npm test
npm run build
npm run test:e2e
# Repository root
dotnet build
dotnet test
git diff --check
```

Browser tests use installed Chrome locally; no browser is installed automatically. CI provisions Playwright Chromium. They use explicit API contract fixtures and real browser rendering, not live LLM/ERP calls. Backend tests retain real loopback HTTP and isolated PostgreSQL coverage in CI.

Browser coverage includes preview invalidation, exact payloads, confirmations, verification, dispatch states, 409, 401/403, SSE casing/deduplication, forms, literal citations and axe/overflow at 375/768/1024/1440px. Unit tests cover HTTP mappings, bounded sessions, fragmented/truncated SSE, cancellation and validation. No lint tool is configured; strict compiler checks and Prettier formatting are used.

## Ask, ingestion and roles

Navigation now includes **Ask with citations** and **Ingest manual**. Connect with a host-issued bearer credential; the header shows the verified role/actor from `/api/identity`. Credentials remain in memory, so reconnect after reload. Conversation content is restored from PostgreSQL, not browser storage.

Create a conversation with provisioned equipment, document and revision IDs. Its response language is fixed at creation from the selected English/Arabic preference. Ask questions independently; history is not implicit model memory. Cancel stops request-owned generation; retry creates a new turn. Source text is displayed verbatim as plain text alongside document/revision/chunk IDs and locator. Refusal is explicit.

Ingest requires the host `ingest` permission. Choose .txt or .pdf, maximum16 MB, title and positive revision number. Retain IDs to retry the same revision. Use Refresh ingestion status to inspect stage outcomes/counts. Uploading is not safety approval. The Technician UI lacks supervisor mutation controls; server checks remain authoritative even if a client bypasses the UI.

Bilingual layouts use existing language persistence and document dir, text-only answer bindings, keyboard-labelled forms, visible status and wrapped provenance identifiers. No new UI framework or remote assets are introduced.
