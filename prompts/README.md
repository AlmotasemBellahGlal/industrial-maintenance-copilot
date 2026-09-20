# Versioned Agent Prompt Library

This directory contains the canonical, reviewed versions of every system prompt
used by the three specialised agents in the Industrial Maintenance Copilot.

## Purpose

- Make prompt changes visible as reviewable diffs in pull requests.
- Provide a version history for each prompt through Git blame/log.
- Allow assessment of prompt evolution alongside code changes.
- Satisfy the assessment requirement for prompts as versioned artifacts.

## Design Approach

Each agent's prompt is stored here as a Markdown file that represents the
**reviewed, canonical source** for the prompt content that is actually sent
to the LLM at runtime.

The C# agent classes (`SymptomMatcherAgent.cs`, `DiagnosticSafetyPlannerAgent.cs`,
`WorkOrderGeneratorAgent.cs`) contain identical prompt text as C# verbatim string
literals. This keeps Docker packaging simple — there are no file-path dependencies
in the published container — while the `prompts/` files provide the diff-visible
version history.

**Divergence between these files and the C# literals is detected automatically
by the prompt contract tests in
`tests/IndustrialCopilot.Application.Tests/Reasoning/PromptVersionTests.cs`.**
Those tests fail if the committed Markdown content does not match the runtime
string, preventing silent drift.

When you change a prompt:
1. Update the Markdown file in the relevant `prompts/<agent>/` directory.
2. Update the corresponding C# string literal in the agent class to match exactly.
3. The contract test will verify they agree.
4. The change appears as a clear diff in both files in your PR.

## Structure

```
prompts/
  README.md                        ← this file
  symptom-matcher/
    v1.md                          ← SymptomMatcherAgent system prompt, version 1
  diagnostic-safety-planner/
    v1.md                          ← DiagnosticSafetyPlannerAgent system prompt, version 1
  work-order-generator/
    v1.md                          ← WorkOrderGeneratorAgent system prompt, version 1
```

## Versioning Convention

Prompt files use a simple `v<N>.md` scheme.

- When a prompt is iterated, create `v2.md` alongside `v1.md` — do not overwrite
  the old version. The old file remains as an audit trail.
- Update the runtime C# string and the contract test version reference.
- Document the reason for the change in the commit message and `docs/AI-USAGE-LOG.md`.

## Runtime Note

`AgentRuntime` appends a fixed **policy suffix** to every agent's system prompt
at runtime. That suffix is not part of these files because it is invariant across
all agents and is tested separately. See `AgentRuntime.cs` for the exact text.

The policy suffix enforces:
- Language/translation rules for the current `responseCulture`.
- `UNTRUSTED DATA` labelling on user input and retrieved tool results.
- No approval, verification, or dispatch authority.
- Return JSON only.
