# Reproducible maintenance demo

This is a **synthetic training scenario**, not a procedure for real equipment. The deterministic provider is a test double, not evidence of real-model diagnostic quality. No OpenAI key or Ollama installation is required.

Token usage from this test provider is synthetic fixture data. No model bill or monetary cost is inferred from it. Real-provider accounting reports only available usage and known costs, grouping currencies separately.

## Prerequisites and startup

Use .NET 10, Node 26/npm 11, Docker with the existing `pgvector/pgvector:pg16` image, and Chrome for local Playwright runs. Keep caches and Docker data on a drive with sufficient space. Do not change machine configuration or download models as part of this demo.

From the repository root in PowerShell:

```powershell
# Local synthetic database only. Bind to loopback, not every network interface.
docker run --detach --name industrial-copilot-issue25 --publish 127.0.0.1:15432:5432 --env POSTGRES_USER=demo --env POSTGRES_PASSWORD=issue25-local-only --env POSTGRES_DB=maintenance_demo pgvector/pgvector:pg16
docker exec industrial-copilot-issue25 pg_isready -U demo -d maintenance_demo
$env:ASPNETCORE_ENVIRONMENT='Development'
$env:DEMO_POSTGRES='Host=127.0.0.1;Port=15432;Database=maintenance_demo;Username=demo;Password=issue25-local-only'
dotnet run --project tools/IndustrialCopilot.Demo -- --demo
```

If the named container already exists, use `docker start industrial-copilot-issue25`. If Windows reserves the port, choose another available loopback port and update the connection string. The initial attempted port `55425` was unavailable locally; `15432` worked.

The separate demo executable refuses to start without both `--demo` and `ASPNETCORE_ENVIRONMENT=Development`. It requires a loopback database named `maintenance_demo`. It registers the normal API, PostgreSQL stores, trusted tools and safety policy, then replaces only `ILlmProvider` with a deterministic provider. Production API/Worker binaries cannot enable this provider through configuration.

Startup applies the existing operations, receiver and knowledge migrations, registers the reviewed equipment/manual revision, and ingests `demo/pump-manual.txt`. Repeated ingestion replaces the same revision; it does not append duplicates. The original text processor creates stable line/scalar locators and chunk identities. The test embedding space has four deterministic features and its own profile/revision; it is not interchangeable with a real embedding model.

The host listens at `http://127.0.0.1:5000`. It writes a randomly generated session credential and Worker configuration under ignored `artifacts/issue25/`. These are local private artifacts; never commit or publish them. Restarting the demo host rotates the credential. No fixed browser bearer credential is embedded in source.

In another terminal:

```powershell
$env:ASPNETCORE_ENVIRONMENT='Development'
$env:DEMO_POSTGRES='Host=127.0.0.1;Port=15432;Database=maintenance_demo;Username=demo;Password=issue25-local-only'
dotnet run --no-build --project tools/IndustrialCopilot.Demo -- --demo --worker
```

In another terminal:

```powershell
cd src/IndustrialCopilot.Web
npm ci --prefer-offline
npm start -- --host 127.0.0.1 --port 4300
```

Open `http://127.0.0.1:4300/connection`. Copy the value of `artifacts/issue25/credential.txt` into **Host bearer credential**. It remains in browser memory only; refresh clears authentication. The language preference alone is persisted. Check API readiness; this checks schema availability, not model quality.

## Canonical scenario

Asset: **DEMO-PUMP-01**, equipment `11111111-1111-1111-1111-111111111111`. Report `pump vibration and seal leakage`.

The original synthetic manual is `22222222-2222-2222-2222-222222222222`, revision `33333333-3333-3333-3333-333333333333`. The trusted deployment procedure is `demo/reviewed-procedures.json`. Its mandatory prerequisite is **Isolate energy and verify zero energy**. For the demo, record only explicitly labelled simulated observations; never claim a real physical check.

1. Start diagnosis. The SSE stream shows the separate Symptom Matcher, Diagnostic & Safety Planner, and Work Order Generator stages.
2. Inspect the advisory explanation and original evidence. The proposed executable scope is **Inspect isolated pump**, action **Inspect seal**.
3. Open the resulting review. The run is `WaitingForApproval`; the work order is `PendingApproval`.
4. Approve the current revision. Approval does not verify safety.
5. Request dispatch before verification. The server rejects it with HTTP 422. This is a real server gate, not a disabled-button demonstration.
6. Reload the review, record the mandatory simulated verification through the separate authorized verification form, and inspect the updated state.
7. Request dispatch. A durable attempt is created, the PostgreSQL demo receiver accepts it, the order becomes `Dispatched`, and the run becomes `Completed`.
8. Inspect the attempt and execution trace. Refreshing an attempt only inspects; it never resends.

For Reject and EditAndApprove, use fresh runs. Edited executable scope is reassessed against the exact trusted procedure; expanding it fails closed. Editing the reported symptom while retaining the reviewed executable scope exercises atomic edited approval. Prior verifications do not transfer. Reusing an old revision/concurrency target returns 409.

## Arabic / English Demo

Supported cultures: `en`, `en-US`, `ar`, `ar-EG`; English is the deterministic fallback. Use the shell's **English | العربية** switch. It updates every route immediately, sets document `lang` and `dir`, and persists locally. Arabic uses RTL layout; identifiers and original source locators are isolated from surrounding bidirectional text.

HTTP and SSE requests propagate `Accept-Language`. The API uses ASP.NET Core request localization and resource-backed safe errors. Machine JSON keys, outcome/status values, error codes, UUIDs, tokens and provenance remain unchanged. Unsupported cultures fall back to English.

Start in English, inspect a proposal, then switch to Arabic. Show the RTL review and unchanged English source citation. Start a new Arabic diagnosis to show the Arabic advisory explanation. The request captures a trusted narrative-language preference; changing UI language does not rewrite an existing run, previously generated narrative, reviewed content or evidence.

**Safety language invariant:** executable instructions and prerequisite definitions remain in their original reviewed language. The exact-scope safety policy does not compare translations. Arabic explanatory narrative is separate from executable content and never grants approval, verification or dispatch permission. Citations remain original source snippets; there is no translated or duplicate vector index.

To demonstrate a localized backend message, call an API route without a credential with `Accept-Language: ar-EG`, then `en-US`. Both return HTTP 401 with `error: unauthenticated`; the title/detail change language.

### خطوات العرض بالعربية

هذا سيناريو تدريبي اصطناعي، وليس إجراء صيانة لمعدة حقيقية. بعد تشغيل قاعدة البيانات وواجهة API والعامل والواجهة كما هو موضح أعلاه:

1. افتح صفحة الاتصال وأدخل بيانات الاعتماد المحلية من الملف `artifacts/issue25/credential.txt`. لا تنشر هذا الملف أو تصوّر قيمته.
2. اختر **العربية**. تحقق من اتجاه الصفحة من اليمين إلى اليسار، ثم أعد تحميلها للتحقق من حفظ اللغة. بيانات الاعتماد لا تُحفظ؛ أدخلها مجدداً عند الحاجة.
3. ابدأ تشخيصاً للمعدة `11111111-1111-1111-1111-111111111111` بوصف «اهتزاز المضخة وتسرب الختم». راقب مراحل الوكلاء الثلاثة ثم افتح مراجعة أمر العمل.
4. راجع الشرح العربي والمصدر الأصلي. تبقى مقتطفات الدليل والتعليمات المعتمدة والمعرّفات بلغتها الأصلية؛ تغيير اللغة لا يغيّر نطاق التنفيذ أو متطلبات السلامة.
5. وافق على المراجعة الحالية، ثم جرّب الإرسال قبل التحقق من السلامة: يجب أن يرفضه الخادم. موافقة المشرف لا تعني استيفاء المتطلبات.
6. أعد تحميل المراجعة وسجّل تحققاً **محاكًى وموسوماً بوضوح** للمتطلب الإلزامي في نموذج التحقق المنفصل. لا تدّعِ إجراء تحقق ميداني حقيقي.
7. أرسل الأمر وتحقق من تأكيد محاولة الإرسال وإكمال التشغيل. اعرض سجل التنفيذ. استخدم تشغيلات جديدة لعرض الرفض والتعديل مع الموافقة وتعارض المراجعة القديمة.
8. بدّل إلى **English** للتحقق من بقاء الأدلة ومتطلبات السلامة والنتائج الآلية كما هي. لعرض شرح مولّد بلغة مختلفة، ابدأ تشخيصاً جديداً بعد اختيار اللغة.

عند ظهور حالة `Uncertain` افحص المحاولة نفسها وانتظر تسوية العامل؛ لا تعِد الإرسال عشوائياً. أوامر الاختبار الآلي أدناه تنفذ القرارات الثلاثة باللغتين دون الحاجة إلى مفتاح OpenAI أو تنزيل نموذج.

## Automated proof

With the demo API running:

```powershell
node tools/demo-smoke.mjs
$env:DEMO_E2E='1'
npm --prefix src/IndustrialCopilot.Web run test:e2e
```

The smoke script creates six actual HTTP/PostgreSQL workflows: all three approval decisions in both cultures. It verifies original citations, identical requirements, stale conflicts, actor-spoof rejection, blocked dispatch before verification, successful verified dispatch, and trace roles. It never seeds approval or dispatch state directly.

The browser integration test uses the real API, database, ingestion and deterministic LLM boundary. Broader browser tests use contract fixtures. Without `DEMO_E2E=1`, only the live integration test is skipped; it must be run explicitly before presentation.

Full validation:

```powershell
$env:RAG_TEST_POSTGRES='Host=127.0.0.1;Port=15432;Database=postgres;Username=demo;Password=issue25-local-only'
dotnet build
dotnet test --no-build -m:1
npm --prefix src/IndustrialCopilot.Web run typecheck
npm --prefix src/IndustrialCopilot.Web test
npm --prefix src/IndustrialCopilot.Web run build
npm --prefix src/IndustrialCopilot.Web audit
git diff --check
```

Run heavy suites sequentially on smaller machines. PostgreSQL fixtures create isolated `rag_test_*` databases; tests include keyword/dense/hybrid rankings, TopK, profile incompatibility, atomic replacement, rollback, cancellation and durable dispatch recovery. Explicit concurrent operations remain enabled inside concurrency tests.

The reconciliation suite simulates receiver acceptance followed by lost confirmation, reconstructs repositories/coordinators, discovers unresolved attempts and reconciles with the original key without a second send. The running Worker uses the same discovery/batch/coordinator implementations. This is a simulated external receiver, not a live ERP validation.

For a live-process recovery demo, restart the demo host with `--demo --uncertain`, restart the Worker with the newly generated configuration, then run `node tools/demo-smoke.mjs --uncertain`. The demo receiver commits acceptance but deliberately reports an uncertain acknowledgement. The script observes `Uncertain`, polls the same attempt, and requires Worker reconciliation to `Confirmed` with the identical external key. No second send is issued by the script or Worker.

Provider HTTP-contract tests cover OpenAI/Ollama completion, streaming, tools, embeddings and fallback boundaries. Live OpenAI/Ollama calls are optional and were not required for this demo. To use an already-installed provider, follow `HOST-SETUP.md` with a separate compatible embedding profile/revision and actual model dimensions; do not reuse the synthetic embedding profile.

## Validation recorded for Issue #25 (2026-09-15)

- .NET: 524 passed, zero failures/skips: Domain 132, Application 178, Infrastructure 151, API 17, Worker 8, PostgreSQL integration 38.
- Angular: 20 unit tests and 26 browser tests passed, including the live API/pgvector bilingual journey. RTL/accessibility checks ran at 375, 768, 1024 and 1440 pixels.
- Six normal and six uncertain-delivery HTTP workflows passed. Four approved uncertain attempts were reconciled by the restarted Worker using their original keys; rejected runs stayed blocked.
- An actual Arabic SSE connection was aborted after WorkflowStarted; the persisted run became Cancelled with cancellation intent recorded and zero published work orders. Cooperative cancellation/timeout races also remain covered by the automated suites.
- .NET and production Angular builds, strict TypeScript checks and whitespace validation passed. npm audit reported zero vulnerabilities. Existing Microsoft.OpenApi NU1903 and non-failing Node runner warnings remain separate.

These results use the synthetic provider and PostgreSQL demo receiver; they do not certify live-model accuracy, physical safety observations or an external ERP connection.

## Reset and troubleshooting

Prefer starting another run; no deletion is needed. Re-ingestion is idempotent for the same manual revision. To reset **only this disposable demo**, stop the demo API and Worker, verify the named container with `docker inspect industrial-copilot-issue25`, then remove that exact container and recreate it using the startup command. There is no production reset endpoint or automatic database deletion in the demo host.

Never run a broad `docker system prune` or delete unrelated databases. Integration-test cleanup only drops generated test databases. Keep private artifacts local and do not screenshot credentials.

- 401: re-enter the current generated credential after host restart/browser reload.
- 403: the host denied the operation; changing UI language or body actor fields cannot grant permission.
- 409: reload and review the current scope before any consequential action.
- 422: inspect authoritative safety/lifecycle state; do not fabricate verification.
- `Uncertain`: inspect the existing attempt and let Worker reconcile; never blindly resend.
- SSE disconnect: inspect the known run. Cancellation intent is not proof execution stopped.
- Disk exhaustion: stop safely; do not delete system/user files. Put development caches and Docker storage on a drive with space.
- Existing `Microsoft.OpenApi` NU1903 remains a separate dependency issue.

## T7 durable job demonstration (Issue #27)

Start the API as above. For the automated crash/recovery proof, stop any separately
started Worker first: the harness owns and kills only its own child processes.
Keep `DEMO_POSTGRES` set and run from the repository root:

```powershell
node tools/t7-smoke.mjs
```

This submits before starting a Worker (202/Queued), retries the same key, cancels a
queued job, starts the real Worker hosted services with an isolated test-only model
pause, kills that process during matching, restarts without a pause and requires a
second execution to publish exactly one unapproved order. It replays persisted SSE,
checks Arabic narrative, cancels active processing and verifies another job still
runs after a blocked job. Finally six English/Arabic Approve/Reject/EditAndApprove
workflows run through durable submission, including stale-token rejection,
verification-gated dispatch and trace links. Proof goes to ignored
`artifacts/t7-proof.json`; logs contain safe identifiers, not credentials.

For interactive API testing, follow the job endpoints in HOST-SETUP. To run the six
approval journeys with a separately running demo Worker:
`node tools/demo-smoke.mjs --jobs`. The unchanged Angular diagnosis page still uses
the legacy request-owned SSE endpoint; use `/api/jobs` for durable semantics.

### بالعربية

شغّل API الديمو أولاً واترك PostgreSQL تعمل. لا تشغّل Worker آخر أثناء
`node tools/t7-smoke.mjs`؛ الاختبار يشغّل عملياته الخاصة ويوقفها لإثبات الاستعادة.
يرجع طلب الوظيفة 202 ومعرّفاً ثابتاً قبل التشخيص. إغلاق بث الأحداث لا يلغي الوظيفة؛
الإلغاء الصريح من `/api/jobs/{jobId}/cancel`. بعد الاستعادة يظل أمر العمل بانتظار
موافقة بشرية، ولا يُسمح بالتسليم قبل تحقق متطلبات السلامة. اللغة تغيّر الشرح فقط.
واجهة Angular الحالية تستخدم المسار القديم؛ اختبار الوظائف الدائمة يتم من API
والسكريبت المذكور. التأخير التجريبي داخل برنامج الديمو فقط، وليس مزوّد الإنتاج.

### T7 local validation checkpoint (2026-09-15)

- .NET build: success, zero errors; eight pre-existing NU1903 warnings unchanged.
- Full .NET suite: 546 passed, zero failures/skips (Domain 132, Application 187,
  Infrastructure 151, API 17, Worker 14, PostgreSQL integration 45).
- Final targeted rerun after review corrections: Application 9 and PostgreSQL 7 passed.
- Angular: 20 unit tests passed on isolated retry; TypeScript check and production
  build passed. Initial Vitest worker startup timed out before executing tests.
- Browser: 26 passed, including the real API/PostgreSQL journey and Arabic/RTL.
- Full T7 process restart/cancellation/SSE replay plus six bilingual durable approval
  journeys passed once. The final repeat with an additional live-observer assertion
  was stopped safely when C: free space fell to about 0.26 GiB. On continuation,
  local Docker Desktop failed at its `dockerInference` socket before starting the
  engine. The final live-observer repeat therefore runs against CI's real pgvector
  service, without repairing or reconfiguring the developer's machine.
- Windows reported a 7,010 MiB allocated page file on C:. No system configuration
  was changed and no user/system files were deleted. Test API, Worker, smoke and
  development frontend processes were stopped; PostgreSQL data was preserved.
- GitHub Actions runs the full .NET suite, the live T7 proof, legacy bilingual HTTP
  smoke and browser integration. The `t7-live-proof` artifact records completed
  process-recovery assertions. CI success is required before final delivery.

## Additional FR-1 corpus proof

The original bilingual pump/approval demo remains unchanged. The separate [synthetic assessment corpus](CORPUS-INGESTION.md) contains 31 documents and 150 actual PDF pages, ingested through the production stage/index pipeline with deterministic test embeddings. Run its `--ingest` twice and `--status` to demonstrate repeatability, Completed status and keyword/dense/hybrid citations. This does not expand the trusted safety-procedure catalog or imply full-stack Compose deployment.

## Product Ask / ingestion — English

Start the existing PostgreSQL + deterministic Demo API and frontend as above. Do not install models or supply paid-provider keys. The supervisor credential artifact now has explicit `ingest`; the technician credential retains read/start only. Never publish those local artifacts.

1. Connect as supervisor and verify the header identity. Open **Ingest manual**.
2. Use equipment `11111111-1111-1111-1111-111111111111`, generate two GUIDs for Document ID and Revision ID, enter a title/revision1 and select a supported text/PDF file. Save those IDs. Upload and inspect Completed plus page/chunk counts.
3. Open **Ask with citations**, create a conversation using the same three IDs, and ask a term present in that source (e.g. `vibration`). Observe live deltas before completion and inspect the exact citation IDs/locator/snippet. Source excerpts are original text; generated answers are advisory.
4. Ask `quasar astrophysics` against the pump source: InsufficientEvidence. Start another grounded question and Cancel answer: generation stops; refresh/select the conversation to inspect the cancelled turn. It has no completed partial answer.
5. Reload, reconnect with the same credential and select the conversation: history returns. Restart the API, reconnect with its newly generated demo credential for the same actor, and inspect again. Completed history survives; interrupted turns expire to Failed after90seconds.
6. Connect as technician: supervisor history is absent, ingestion is unavailable and approval/verification/dispatch controls are unavailable. Server-side requests remain forbidden. Technician can create its own Ask conversation on permitted equipment.
7. Switch Arabic **before creating** a new conversation for Arabic answer context. Existing conversations preserve their culture. Deterministic demo emits a localized advisory frame and original source excerpts; it is a streaming test double, not translation/semantic-quality evidence.

## تجربة المنتج — العربية

شغّل PostgreSQL ومضيف Demo والواجهة بالطريقة الموضحة أعلاه. لا تحتاج إلى مفتاح مدفوع أو تنزيل نماذج.

1. اتصل ببيانات المشرف المحلية، وتأكد من ظهور الهوية. افتح **إدخال دليل**.
2. استخدم معرف المعدة التجريبية أعلاه، ومعرفَي GUID جديدين للمستند والإصدار. أدخل العنوان ورقم الإصدار واختر ملفًا نصيًا أو PDF لا يتجاوز16 ميجابايت. احتفظ بالمعرفات، ثم ارفع الملف وتأكد من حالة **مكتمل** والأعداد.
3. اختر العربية ثم افتح **اسأل مع المراجع** وأنشئ محادثة بنفس المعرفات. اسأل عن كلمة موجودة في الدليل، مثل «اهتزاز» في مصدر عربي. راقب الإجابة التدريجية والمراجع الأصلية.
4. السؤال غير المدعوم يُظهر أدلة غير كافية. زر **إلغاء الإجابة** يوقف توليد Ask. هذا مختلف عن فصل مراقبة وظيفة T7، التي تستمر حتى إلغاء صريح.
5. أعد تحميل الصفحة، وأعد الاتصال بنفس الحساب ثم اختر المحادثة لاستعادة السجل. لا تُحفظ بيانات الدخول في المتصفح.
6. جرّب حساب الفني: لا يمكنه قراءة محادثات المشرف أو رفع الأدلة أو تنفيذ إجراءات المشرف. يبقى اعتماد الإنسان والتحقق الإلزامي من السلامة شرطين مستقلين للتنفيذ.

For automated proof, run `node tools/product-smoke.mjs` with the demo host running; `--verify-history` verifies the saved proof conversation after restarting that host. Browser journeys are `product-integration.spec.ts` with `DEMO_E2E=1`. Existing `tools/t7-smoke.mjs` remains the durable-disconnect/recovery and D5 regression.

### Measured Issue #35 proof (2026-09-16)

The deterministic real PostgreSQL/API proof produced seven provider deltas in each language. English deltas arrived at 229, 689, 791, 903, 1003, 1103 and 1255 ms; completion at 1413 ms. Arabic deltas arrived at 135, 586, 701, 800, 901, 1017 and 1167 ms; completion at 1317 ms. These are observations, not latency SLAs. The iterator processes evidence on demand and never calls a completed-answer method before streaming. OpenAI/Ollama protocol compatibility is covered by adapter tests; no paid/local model was invoked for this proof.

Both uploaded revisions reached Completed; exact citations included `text:lines 1-5; scalars 1-193` and unchanged source excerpts. Unsupported questions produced InsufficientEvidence with zero answer deltas. Cancelling after the first delta caused the demo provider to log `cancelled=True; deltas=1` and persisted a Cancelled turn with no partial final answer. Restarting the API and running `--verify-history` preserved final answers and exact citations. The real Angular journeys passed in both languages; the existing approval/verification/dispatch browser journey also passed.

Run live harnesses sequentially using the same demo identity: actor rate limits remain enabled. A 429 is not an approval/safety failure. The Node smoke helper honors bounded Retry-After only for explicit pre-execution rejection; the product does not silently retry Ask. The T7 harness separately proved observer disconnect, actual Worker termination/restart, replayed progress, explicit cancellation and six bilingual D5 approval/safety journeys.
