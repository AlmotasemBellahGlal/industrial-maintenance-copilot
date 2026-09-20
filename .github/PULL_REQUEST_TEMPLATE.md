## Summary

<!--
Briefly describe what this PR changes and why.
Link the issue: "Closes #<number>"
-->

Closes #

## What changed

<!--
List the main changes. Be specific enough that a reviewer can understand
the scope without reading every file.
-->

-
-

## Why

<!--
Explain the motivation. Reference the issue, business requirement (BR-xx), or
architectural decision (ADR-xxx) that drives this change.
-->

## How it was tested

<!--
Describe exactly how you verified the change works correctly.
Include: test commands run, outputs observed, docker commands used.
-->

- [ ] `dotnet build` — 0 warnings/errors
- [ ] `dotnet test` — all tests pass
- [ ] `python3 scripts/security/scan.py secrets` — no findings
- [ ] Other (describe):

## Security impact

<!--
Does this change affect authentication, authorization, agent prompts,
safety policy, approval gates, or secret handling?
If yes, describe exactly how the security model is preserved.
-->

No security-sensitive change / [describe impact]

## Documentation impact

<!--
Does this change require updates to README, DEPLOYMENT.md, ARCHITECTURE.md,
ADRs, or other docs? List what was updated.
-->

No documentation change needed / [list docs updated]

## AI usage

<!--
If AI assisted with this change, summarise:
- What was delegated to AI
- What human constraints were given
- AI mistakes found and corrected
- Self-review findings
If no AI was used, write "None".
-->

None / [describe AI usage]

## Checklist

- [ ] Issue linked with `Closes #<N>`
- [ ] Tests added or updated for every logic change
- [ ] No new secrets, credentials, or `.env` files committed
- [ ] `evaluation/golden-v1.json` not modified
- [ ] Architecture invariants in `.kiro/steering/architecture-rules.md` respected
- [ ] Self-reviewed my own diff
- [ ] CI passes
