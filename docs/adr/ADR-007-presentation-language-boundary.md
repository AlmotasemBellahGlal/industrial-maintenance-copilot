# ADR-007: Presentation language remains outside executable safety scope

Status: Accepted for Issue #25

The user requires English/Arabic UI, API errors and AI narrative without changing authorization, citations or safety outcomes. The existing safety policy matches exact reviewed executable text; translating that text would change scope and could invalidate approval.

The API selects a supported request culture with the standard Accept-Language provider. It passes an explicit, validated response culture to the Application workflow. Agents may localize advisory matched-symptom explanations. Executable descriptions, actions, authoritative prerequisite definitions and original evidence remain unchanged. The workflow returns the narrative separately; Domain types acquire no presentation fields.

The Angular shell stores only language preference, uses a shared catalog/translation pipe, propagates culture centrally for HTTP and SSE, and isolates technical identifiers in RTL layouts. API error codes/statuses/JSON keys remain stable while safe resource-backed text is localized. Existing generated content is not silently translated on UI switching.

Rejected: culture-specific Domain models, translating exact-scope text, translated machine statuses, fabricated translated snippets, a second vector index for UI language, and production registration of a fake provider. The deterministic demo provider belongs only to a separately guarded development executable.
