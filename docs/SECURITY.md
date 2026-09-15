# Security: Industrial Maintenance Copilot

This document maps implemented controls to evidence for Issue #33. It is not a penetration-test certification. IMPLEMENTED means a concrete control exists; PARTIAL identifies a useful boundary with residual gaps; DEFERRED is not claimed. Taxonomy references are OWASP Web Top 10:2025, API Security Top 10:2023, and LLM Top 10:2025 (edition pinned for stable assessment numbering).

## 1. Security objectives

Prevent unauthorized equipment access and irreversible maintenance actions; keep approval bound to current executable scope; prevent model content from creating authority; bound expensive execution; minimize outbound sensitive text. Preserve D5, durable T7 jobs and FR-1/FR-3 provenance.

## 2. Assets

Equipment/manual identities, exact revisions/chunks/citations, executable work scope, approval history, trusted safety observations, dispatch tickets, durable jobs, configuration credentials, user text and traces.

## 3. Trust boundaries

Browser -> authenticated API -> resource-authorized Application -> Domain invariants -> transactional PostgreSQL. A separate worker claims durable jobs and rechecks authority. Trusted operator configuration selects procedures, credentials, models and endpoints. User questions, manuals, retrieved text and model/tool-call outputs are untrusted. The hosted provider is an external data recipient; a local provider is a separate operator-controlled process, not a trusted decision-maker.

## 4. Threat actors

Unauthenticated clients; authenticated technicians exceeding their permissions; users guessing resource IDs; malicious manual authors; compromised/model-confused providers; stolen credentials; compromised dependencies; privileged operator mistakes. A compromised OS/database administrator is outside this application's containment boundary.

## 5. Data classification

Synthetic demo/evaluation content is public fictional data. Production manuals, questions and safety evidence can be confidential. Credentials are secret. Trace DTOs contain bounded operational metadata, not raw prompts, tool bodies or hidden reasoning. Database retention/encryption and access to authoritative personal data remain deployment responsibilities.

## 6. Authentication

IMPLEMENTED: HostAuthentication accepts configured bearer credentials, validates configuration, compares hashes in constant time, and supplies trusted actor/permission/equipment claims. Unknown credentials fail closed. HostCredential.ToString does not expose fields. No production default credential, session cookie or frontend-supplied identity is trusted.

PARTIAL: this is an explicit host-credential scheme, not OIDC/MFA. Rotation/revocation requires host configuration/restart. Edge-level authentication-failure throttling, identity lifecycle and MFA are deployment work; authenticated mutation limiting does not limit unauthenticated requests rejected earlier by authorization.

## 7. Authorization / resource ownership

IMPLEMENTED: MaintenanceEndpoints.Permit/Order, ReasoningJobEndpoints.Read, HostAccess and trusted action authorization check permissions AND equipment scope. Job inspection/cancellation/SSE check equipment; progress polls recheck the authenticated resource scope. Trace-store access resolves the run's equipment. Shared authorized equipment is the access unit: users are not restricted to personally created jobs. Object existence can still be distinguishable (403 versus 404); no claim of resource-existence concealment.

The opt-in Development-only demo now creates two independent random ephemeral credentials:

| Identity | Permissions | Credential artifact |
|---|---|---|
| demo-technician | read,start | artifacts/issue25/technician-credential.txt |
| demo-supervisor | read,start,approve,verify,dispatch | artifacts/issue25/credential.txt |

Both are restricted to the canonical demo equipment. Tokens/configuration are ignored, never committed or printed. Use the connection screen to switch tokens; the technician cannot approve, verify or dispatch. Restarting the demo generates new tokens. Never deploy the demo executable. Run `python scripts/security/demo_roles.py` against the running isolated demo for a real PostgreSQL role/safety proof; it creates one synthetic review and never prints credentials.

## 8. Human approval boundary

IMPLEMENTED: decisions use authenticated host actor, explicit approve permission and current revision/concurrency token. Typed request binding rejects extra actor/approval fields. Approve/Reject/EditAndApprove remain trusted Application actions; immutable approval history records the decision. A model proposal is not an approval.

## 9. Safety boundary

IMPLEMENTED: WorkOrder owns transitions; current authoritative assessment, current supervisor approval and satisfied mandatory verified requirements are rechecked at dispatch. Edits invalidate stale approval/verification. Trusted verification requires verify permission. Durable dispatch reservation/idempotency and receiver reconciliation prevent duplicate publication. No safety-critical text is silently truncated or redacted in Domain state.

## 10. Prompt-injection defense

IMPLEMENTED structural boundary: AgentRuntime places trusted policy in System; user input stays User; retrieval stays Tool evidence. It explicitly marks user/manual/tool text UNTRUSTED DATA. Only fixed role tools are available, and every call is checked before execution. Tests inject malicious text in both user and retrieved content and attempt dispatch; no dispatch tool executes.

PARTIAL semantic defense: models can still produce misleading narrative or irrelevant answers. Prompt wording is defense in depth, not the authorization mechanism. FR-3's poor baseline and injection fixtures remain frozen and unchanged; this issue does not claim perfect refusal or semantic resistance.

## 11. Retrieved-content trust model

IMPLEMENTED: revision-filtered retrieval plus EvidenceCatalog resolves opaque model references to original document/revision/chunk/locator/snippet. Retrieved instructions cannot add permissions or change reviewed procedures. Hosted redaction affects outbound copies only; returned citation provenance still comes from the original catalog, never a rewritten provider snippet. Existing cross-platform PDF newline/derived-ID limitation remains as documented in EVALUATION.md.

## 12. Tool/output handling

IMPLEMENTED: closed tool registry, role-specific allowlist, exact JSON keys/types, candidate index bounds, duplicate call-ID rejection, bounded JSON depth and strict structured result parsing. Only Symptom Matcher can request retrieve_evidence; planners/generators have no model-executable side-effect tool. Trusted tools reauthorize against host context. No model shell, SQL, arbitrary path, raw HTML or automatic dispatch execution exists. Angular uses escaped bindings; CSP adds defense in depth.

## 13. PII/sensitive-data handling

PARTIAL: HostedDataBoundary redacts common email, formatted/international phone, SSN-shaped values, labeled national IDs, bearer strings, password/secret/API-key-like text and the configured API key before hosted transmission. JSON messages/tool arguments are processed as JSON copies where possible, preserving keys, types and correlation IDs. Regexes have a timeout. Tests verify redaction, JSON validity, no caller mutation and GUID preservation.

This is pattern-based minimization, not universal PII discovery: names, addresses, arbitrary ID formats, encoded/obfuscated secrets and sensitive industrial facts may pass. Numeric safety facts are deliberately not broadly masked. Redaction can reduce model answer quality; deterministic reviewed-scope validation must reject any altered executable instruction. Raw user/manual content can remain in the authorized operational/knowledge database. Logs/traces do not intentionally contain it; retention, encryption and a full DLP program are deferred.

## 14. Provider data exposure

OpenAI: fixed HTTPS api.openai.com endpoint, no redirects/cookies; outbound message content, tool argument history and embedding inputs pass through HostedDataBoundary. Original local records are not mutated. Trusted model identifiers, tool schema/description metadata and opaque IDs are sent; do not put secrets into trusted schema/configuration text. Redacted user/evidence text still contains industrial information. Provider retention/region/account policy must be assessed before real manuals are sent.

Ollama: no hosted API key; local/explicitly configured endpoint receives original content without redaction. Treat any remote Ollama endpoint as a separate data disclosure boundary. Operators must secure its network and deployment. Both providers' outputs remain untrusted. Embedding providers have separate configuration and profile/binding checks; KnowledgeRegistration incorporates hosted-redaction-v1 into the hosted index binding, rejecting pre-redaction indexes rather than silently mixing vectors; existing hosted deployments require an explicit new embedding profile/reindex plan. No existing production index is migrated automatically.

## 15. Secrets management

IMPLEMENTED: environment/configuration supplies secrets; no default production tokens. Demo tokens are generated at launch. Full-history scan currently reports no findings; no history rewrite or scanner suppression is used. Scanner output is redacted and ignored locally. A real finding requires credential rotation/review before any destructive history operation. Local ignored files are not automatically encrypted.

## 16. Rate limiting / resource controls

IMPLEMENTED: ASP.NET Core fixed-window limiter applies to all authenticated API POSTs, including runs/stream, job submissions/cancellation, approval/verification/dispatch. Default 30 permits per 60 seconds, zero queue; partition is trusted actor across resources/endpoints so rotating IDs cannot bypass it. Configuration: Security:MutationPermits (1..300), Security:WindowSeconds (1..3600). Rejections return localized rate_limited/429 and Retry-After. GET health/readiness remain outside this budget.

PARTIAL: in-memory per instance; multiple replicas multiply allowance, fixed windows allow boundary bursts, authorized users can exhaust their own budget including cancellation. No global distributed quota or concurrent SSE admission limit. Deploy a trusted gateway/distributed limiter for public scale, including unauthenticated traffic. Client IP and X-Forwarded-For are not trusted partition inputs.

| Surface | Enforced bound |
|---|---|
| Kestrel request body | 128 KiB; payload_too_large/413 |
| HTTP symptom | 2,000 characters; invalid_request/400 |
| Agent symptom/candidates | 4,000 characters / 8 candidates |
| Initial agent JSON / accumulated message context | 262,144 / 524,288 characters |
| Agent result / individual tool arguments | 65,536 / 16,384 characters |
| Model turns / tool calls | configurable 1..16 each; defaults in ReasoningLimits |
| Retrieval TopK | ReasoningLimits maximum 20; candidate/fusion bounds in store configuration |
| Agent output tokens | 4,096 requested |
| Provider chat output | default 4,096; explicit maximum 16,384 |
| Serialized provider input / message count | 1,048,576 characters / 128 |
| Non-stream provider response / SSE event | 4 MiB / 1 Mi characters |
| PDF/text source | 16,000,000 bytes / 4,000,000 extracted characters; PDF 500 pages |
| Evidence catalog | 256 entries; snippet 16,000, locator 4,000 characters |
| Job attempts / worker concurrency | 3 attempts / configured maximum 4 workers per host |
| Progress pages / legacy stream queue | 64 events per read / bounded configured queue |

Bounds fail closed, never silently truncate executable scope. Overall provider deadlines and existing T7 retry backoff/lease cancellation remain in force. Transport input size is checked after serialization; it is not a guarantee against already allocated caller objects. Ollama embedding calls share the serialized-input/non-stream response bounds and retain truncate=false; broader aggregate provider memory accounting is deferred.

## 17. Input/file security

IMPLEMENTED: closed HTTP JSON, GUID/text/collection validation; ingestion is a trusted operator CLI accepting local paths, not an untrusted HTTP file/path endpoint. Only selectable-text PDF and UTF-8 text are allowed. Size/page/character limits and all-or-fail extraction remain. No OCR, URL fetching, PDF script/attachment execution or archive extraction. Parser CPU sandboxing and antivirus are deferred.

## 18. Database/injection controls

IMPLEMENTED: repository SQL uses parameterized values for identifiers-as-data, text, vectors and queries. SQL clauses are trusted fixed code; no HTTP input becomes SQL syntax. PostgreSQL transactions protect index replacement, jobs and dispatch. Integration suites exercise payload persistence and concurrency; this issue does not invent a fake SQL-execution test for nonexistent endpoints. Database least-privilege users/TLS/backups are deployment obligations (the local synthetic demo uses its isolated test database).

## 19. Browser/API security configuration

IMPLEMENTED: CORS denies cross-origin by default. Security:AllowedOrigins is an explicit validated origin array; only required methods/headers exposed, no wildcard or credentialed cookies. API responses set nosniff, DENY framing, no-referrer, no-store and default-src none CSP. Production API requires HTTPS; untrusted forwarded headers do not override it. HSTS is enabled outside Development/Testing for applicable HTTPS hosts. Development OpenAPI remains disabled in production.

Angular index has CSP and no-referrer meta: self scripts/fonts, no objects, restricted base/form actions, self plus loopback demo connections; inline styles are needed by Angular. Production should use same-origin proxying and serve frame-ancestors/HSTS as host headers (meta cannot enforce frame-ancestors). This repository does not add a production static host. A reverse proxy must provide TLS and explicitly trusted forwarding if TLS terminates before Kestrel; arbitrary X-Forwarded-* is deliberately not enabled. Local HTTP is limited to Development/Testing loopback. Strict no-inline-style CSP/nonces and deployment CSP reporting are deferred.

## 20. Security logging/auditing

IMPLEMENTED: HTTP security logs use method, matched route template, status and GUID correlation only; no URL values, headers, request body or exception object. This covers 401/403/429, decisions, verification, dispatch and cancellation outcomes. Approval history and dispatch/job records provide durable business audit; existing provider failures expose safe classifications and traces omit prompts/hidden reasoning. Malformed requests may have no correlation if rejected by Kestrel before middleware. Central alerting, immutable external audit storage and operator retention policies are deferred.

## 21. Supply-chain security

PARTIAL: pinned normal NuGet references/npm lockfile plus scans reduce known dependency risk. Provider configuration is privileged deployment input; no automatic model install or output execution. Model weights are not security-audited. Dataset/manual poisoning and compromised upstream dependencies are not eliminated by scanners.

## 22. Dependency scanning

Run `python scripts/security/scan.py dependencies` from repo root after dotnet restore. It scans transitive NuGet dependencies and the entire npm lockfile including dev dependencies. CI fails on High/Critical; lower findings remain in JSON for review. Scanner/tool/feed errors fail closed. Reports: artifacts/security/nuget.json and npm.json. Microsoft.OpenApi upgraded from transitive 2.0.0 to explicit compatible 2.7.5, the patched 2.x version for GHSA-v5pm-xwqc-g5wc. The vulnerability is not suppressed. OpenAPI schema generation has an HTTP regression test.

## 23. Secret scanning

Run `python scripts/security/scan.py secrets`. Gitleaks 8.30.1 is downloaded to ignored artifacts and verified with pinned SHA256 for Windows/Linux x64. It scans `git --all` history with redacted output. CI checks out fetch-depth 0. No allowlist suppressions are configured. Detection is signature-based and cannot prove absence of all secrets. No automatic history rewriting or credential testing occurs.

## 24. Security tests

ApiTests covers role difference, actor/resource partition limiting, CORS, headers, body/question limits, spoofed fields, safe errors, HTTPS forwarding assumptions and patched OpenAPI. JobHttpTests covers unauthorized job inspect/cancel/SSE and actor spoofing against real PostgreSQL. AgentSafetyTests covers hostile direct/retrieved text, unauthorized tools, malformed output, citation forgery, duplicate calls and loop limits. SecurityBoundaryTests covers redaction/JSON integrity, local-versus-hosted exposure and transport budgets. ProviderFailureTests cover safe exceptions. Existing Domain/dispatch/approval/job integration suites remain mandatory. FR-3 frozen files are checked unchanged, not rewritten for a better security score.

## 25. Residual risks / deferred controls

No perfect prompt-injection resistance, full PII detector, production identity provider, distributed rate limiter, edge DDoS protection, provider retention guarantee, secure model-weight audit, process-isolated PDF parser, automatic credential rotation or production Angular host is claimed. Security controls do not turn poor RAG answers into trustworthy procedures; authoritative policy/human/physical verification still governs action. See the matrix for status rather than treating all OWASP categories as solved.

## 26. Threat -> control -> evidence matrix

| Threat / OWASP mapping | Attack surface | Control/status | Implementation evidence | Verification evidence | Residual risk |
|---|---|---|---|---|---|
| Broken access control (Web A01, API1/3/5) | resource IDs, decisions, jobs/SSE | IMPLEMENTED permission+equipment checks | MaintenanceEndpoints, ReasoningJobEndpoints, HostAccess | ApiTests, JobHttpTests, HttpApprovalTests | existence distinctions; shared equipment scope |
| Authentication failure (Web A07, API2) | bearer credential | PARTIAL validated constant-time matching | HostAuthentication | missing/invalid credentials, two-role tests | no MFA/OIDC or edge brute-force limiter |
| Unrestricted consumption (API4/6, LLM10) | POSTs, prompts, provider bodies | IMPLEMENTED bounded requests/actor budget; PARTIAL distributed protection | ApiSecurity, AgentRuntime, ChatCompletionsClient | 429 partition/body/budget tests | replicas, burst windows, SSE connections |
| Misconfiguration (Web A02, API8) | browser, HTTP proxy | PARTIAL explicit CORS, API headers/HTTPS, frontend CSP | Program, ApiSecurity, index.html | CORS/header/HTTPS tests, browser CI | static-host headers and trusted TLS termination |
| Injection (Web A05) | SQL, JSON, operator input | IMPLEMENTED parameterized SQL/closed DTOs | Postgres stores, HttpContracts, AgentJson | real PG suites, malformed/spoofed request tests | trusted operator/DB configuration |
| Supply chain (Web A03, LLM03) | packages, model host | PARTIAL pinned scans, compatible patch | scan.py, CI, Api csproj | full-history and dependency scans | unknown vulnerabilities/model weights |
| Logging/exception failure (Web A09/A10) | failures, decisions | PARTIAL safe correlated logs/business records | Program, ProviderFailures, workflow stores | safe exception and D5/T7 tests | no external immutable audit/SIEM |
| SSRF (API7) | provider/ingestion endpoints | IMPLEMENTED fixed hosted endpoint, no HTTP ingest URLs | OpenAiOptions, operator-only ingestion | provider configuration tests | privileged local-provider endpoint config |
| Prompt injection (LLM01) | user and retrieved text | IMPLEMENTED privilege separation; PARTIAL semantic resistance | AgentRuntime, trusted tools | direct+indirect unauthorized dispatch test | misleading narrative still possible |
| Sensitive disclosure (LLM02/07) | hosted messages/embeddings, traces | PARTIAL copy-only redaction and no prompt logging | HostedDataBoundary, WorkflowTrace | redaction, provenance and safe exception tests | unrecognized PII, industrial facts, system-policy text not secret |
| Data poisoning/vector weaknesses (LLM04/08) | indexed manuals/revisions | PARTIAL trusted ingest, profile/revision checks, exact citations | ingestion/store/EvidenceCatalog | FR-1, FR-3, profile/revision tests | authorized poisoned corpus; no content authenticity certification |
| Improper output handling (LLM05) | model JSON/tool calls/frontend | IMPLEMENTED strict types/no execution | AgentJson, tool registry, Angular bindings | malformed output/tool tests, browser tests | semantic correctness not guaranteed |
| Excessive agency (LLM06) | model side effects | IMPLEMENTED no dispatch tools, host authorization, D5 gate | AgentRuntime, WorkOrder, DispatchCoordinator | agent/D5/approval/dispatch suites | compromised trusted human/host |
| Misinformation (LLM09) | advisory narrative | PARTIAL citations + measured evaluation + human gate | EvidenceCatalog, exact policy, FR-3 | frozen poor baseline + safety tests | no general truth guarantee |

References: [OWASP Web 2025](https://top10.owasp.org/2025/), [API 2023](https://api-security.owasp.org/editions/2023/en/0x00-header/), [LLM 2025](https://owasp.org/www-project-top-10-for-large-language-model-applications/assets/PDF/OWASP-Top-10-for-LLMs-v2025.pdf), [Microsoft advisory](https://github.com/microsoft/OpenAPI.NET/security/advisories/GHSA-v5pm-xwqc-g5wc), [Gitleaks release](https://github.com/gitleaks/gitleaks/releases/tag/v8.30.1).
