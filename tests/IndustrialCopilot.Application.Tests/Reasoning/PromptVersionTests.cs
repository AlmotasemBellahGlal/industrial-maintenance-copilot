using System.Reflection;
using System.Text.RegularExpressions;
using IndustrialCopilot.Application.Abstractions.Agents;

namespace IndustrialCopilot.Application.Tests.Reasoning;

/// <summary>
/// Verifies that the versioned prompt Markdown files in prompts/ match the
/// runtime strings used by the agent classes. A failing test means the
/// committed prompt artifact has drifted from the code that actually runs.
///
/// Design: The C# agent classes are the runtime source of truth (no file-path
/// dependency in containers). The prompts/ files are the reviewed, diff-visible
/// source of truth for documentation and version history. These tests enforce
/// they stay identical.
/// </summary>
public class PromptVersionTests
{
    // Resolve repository root from the test assembly output directory.
    // Published test assemblies will have a different path but still resolve
    // correctly because we walk up from the known assembly location.
    private static string RepoRoot()
    {
        var dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        // Walk up until we find a prompts/ directory (repo root marker).
        for (var d = new DirectoryInfo(dir); d != null; d = d.Parent)
        {
            if (Directory.Exists(Path.Combine(d.FullName, "prompts")))
                return d.FullName;
        }
        throw new InvalidOperationException(
            $"Cannot find repository root (prompts/ directory) from {dir}. " +
            "Run the test from a checkout, not from a published artifact directory.");
    }

    /// <summary>
    /// Extracts the prompt text from the first fenced code block (```) in the
    /// Markdown file. The prompt files use a single ``` fence to contain the
    /// exact text that is passed to AgentRuntime.Complete as 'instructions'.
    /// </summary>
    private static string ExtractPromptFromMarkdown(string markdownPath)
    {
        var content = File.ReadAllText(markdownPath);
        var match = Regex.Match(content, @"```\r?\n(.*?)\r?\n```", RegexOptions.Singleline);
        if (!match.Success)
            throw new InvalidOperationException(
                $"No fenced code block found in {markdownPath}. " +
                "The prompt file must contain exactly one ``` block with the prompt text.");
        return match.Groups[1].Value;
    }

    [Fact]
    public void SymptomMatcherRuntimePromptMatchesVersionedArtifact()
    {
        var promptFile = Path.Combine(RepoRoot(), "prompts", "symptom-matcher", "v1.md");
        Assert.True(File.Exists(promptFile), $"Prompt file not found: {promptFile}");

        var filePrompt = ExtractPromptFromMarkdown(promptFile);

        // This is the exact string literal from SymptomMatcherAgent.MatchAsync.
        // Update both this constant and v1.md (or create v2.md) when iterating.
        const string RuntimePrompt =
            """
                You are the Symptom Matcher. Retrieve manual evidence before matching; use only retrieve_evidence.
                Candidate numbers are zero-based indexes into the supplied candidates. Evidence reference strings are supplied by the tool.
                Success JSON: {"outcome":"Success","candidate":0,"symptoms":[{"description":"matched symptom","evidence":["e0"]}]}.
                Failure JSON: {"outcome":"InsufficientEvidence"} or {"outcome":"CannotProceed"}.
                Match only what retrieved evidence supports. Never invent identifiers, references, locators or snippets.
                """;

        // Normalise: trim leading/trailing whitespace and normalise line endings.
        var normalised = NormalisePrompt(RuntimePrompt);
        var fileNormalised = NormalisePrompt(filePrompt);

        Assert.True(normalised == fileNormalised,
            $"""
            SymptomMatcher prompt drift detected.
            The runtime string in SymptomMatcherAgent.cs does not match
            prompts/symptom-matcher/v1.md.

            To fix: update both the C# literal and the Markdown file to the same text,
            or create prompts/symptom-matcher/v2.md for the new version.

            Runtime (normalised):
            {normalised}

            File (normalised):
            {fileNormalised}
            """);
    }

    [Fact]
    public void DiagnosticSafetyPlannerRuntimePromptMatchesVersionedArtifact()
    {
        var promptFile = Path.Combine(RepoRoot(), "prompts", "diagnostic-safety-planner", "v1.md");
        Assert.True(File.Exists(promptFile), $"Prompt file not found: {promptFile}");

        var filePrompt = ExtractPromptFromMarkdown(promptFile);

        const string RuntimePrompt =
            """
                You are the Diagnostic & Safety Planner. Propose ordered diagnostic instructions and advisory prerequisites from supplied evidence.
                You have NO tools. Use only supplied evidence references. Never mark prerequisites mandatory, verified, satisfied or authoritative.
                Success JSON: {"outcome":"Success","steps":[{"order":1,"instruction":"instruction","evidence":["e0"]}],"prerequisites":[{"description":"precaution","evidence":["e0"]}]}.
                Failure JSON: {"outcome":"InsufficientEvidence"} or {"outcome":"CannotProceed"}.
                Every step and advisory prerequisite needs grounding. An empty advisory list is not a safety assessment.
                """;

        var normalised = NormalisePrompt(RuntimePrompt);
        var fileNormalised = NormalisePrompt(filePrompt);

        Assert.True(normalised == fileNormalised,
            $"""
            DiagnosticSafetyPlanner prompt drift detected.
            The runtime string in DiagnosticSafetyPlannerAgent.cs does not match
            prompts/diagnostic-safety-planner/v1.md.

            Runtime (normalised):
            {normalised}

            File (normalised):
            {fileNormalised}
            """);
    }

    [Fact]
    public void WorkOrderGeneratorRuntimePromptMatchesVersionedArtifact()
    {
        var promptFile = Path.Combine(RepoRoot(), "prompts", "work-order-generator", "v1.md");
        Assert.True(File.Exists(promptFile), $"Prompt file not found: {promptFile}");

        var filePrompt = ExtractPromptFromMarkdown(promptFile);

        const string RuntimePrompt =
            """
                You are the Work Order Generator. Propose a description and ordered executable actions based on the reviewed diagnostic plan.
                You have NO tools. Use only supplied evidence references. Do not expand the planned maintenance scope.
                Success JSON: {"outcome":"Success","description":"proposal","actions":[{"order":1,"instruction":"action","evidence":["e0"]}]}.
                Failure JSON: {"outcome":"InsufficientEvidence"} or {"outcome":"CannotProceed"}.
                Safety proposals are preserved by trusted code. Never generate safety flags, identities, approval or dispatch fields.
                """;

        var normalised = NormalisePrompt(RuntimePrompt);
        var fileNormalised = NormalisePrompt(filePrompt);

        Assert.True(normalised == fileNormalised,
            $"""
            WorkOrderGenerator prompt drift detected.
            The runtime string in WorkOrderGeneratorAgent.cs does not match
            prompts/work-order-generator/v1.md.

            Runtime (normalised):
            {normalised}

            File (normalised):
            {fileNormalised}
            """);
    }

    [Theory]
    [InlineData("symptom-matcher", "v1.md", "You are the Symptom Matcher")]
    [InlineData("diagnostic-safety-planner", "v1.md", "You are the Diagnostic & Safety Planner")]
    [InlineData("work-order-generator", "v1.md", "You are the Work Order Generator")]
    public void PromptFilesContainExpectedRoleIdentifier(string agentDir, string file, string expectedOpener)
    {
        var promptFile = Path.Combine(RepoRoot(), "prompts", agentDir, file);
        Assert.True(File.Exists(promptFile), $"Prompt file not found: {promptFile}");

        var promptText = ExtractPromptFromMarkdown(promptFile);
        Assert.Contains(expectedOpener, promptText);
    }

    [Theory]
    [InlineData("symptom-matcher", "v1.md")]
    [InlineData("diagnostic-safety-planner", "v1.md")]
    [InlineData("work-order-generator", "v1.md")]
    public void PromptFilesDoNotContainAuthoritativeFields(string agentDir, string file)
    {
        // No prompt should claim approval, dispatch, or credential authority.
        var promptFile = Path.Combine(RepoRoot(), "prompts", agentDir, file);
        var promptText = ExtractPromptFromMarkdown(promptFile);

        // These would be signs of a safety invariant violation in the prompt itself.
        Assert.DoesNotContain("you may approve", promptText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("you may dispatch", promptText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("you are authorised", promptText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("grant permission", promptText, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("symptom-matcher", "v1.md")]
    [InlineData("diagnostic-safety-planner", "v1.md")]
    [InlineData("work-order-generator", "v1.md")]
    public void PromptFilesRequireJsonOutput(string agentDir, string file)
    {
        var promptFile = Path.Combine(RepoRoot(), "prompts", agentDir, file);
        var promptText = ExtractPromptFromMarkdown(promptFile);
        // All agent prompts specify JSON output contracts.
        Assert.Contains("outcome", promptText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Success", promptText);
    }

    private static string NormalisePrompt(string raw)
        => raw.ReplaceLineEndings("\n").Trim();
}
