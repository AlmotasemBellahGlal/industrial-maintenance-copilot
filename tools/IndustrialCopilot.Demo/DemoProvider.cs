using System.Text.Json;
using IndustrialCopilot.Application.Abstractions.AI;
using IndustrialCopilot.Application.Abstractions.AI.Models;

namespace IndustrialCopilot.Demo;

/// <summary>Deterministic test double, not an industrial diagnostic model. Only this separate demo executable registers it.</summary>
internal sealed class DemoProvider(int delayMilliseconds=0) : ILlmProvider
{
    public async Task<ToolCompletionResponse> CompleteWithToolsAsync(CompletionRequest request,IReadOnlyList<ToolDefinition> tools,CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        // Explicit test-only pause for crash/cancellation demonstrations, absent from production providers.
        if(delayMilliseconds>0) await Task.Delay(delayMilliseconds,ct);
        var system=request.Messages[0].Content;
        if(system.Contains("You are the Symptom Matcher",StringComparison.Ordinal))
        {
            using var input=JsonDocument.Parse(request.Messages.First(m=>m.Role==LlmRole.User).Content);
            var symptom=input.RootElement.GetProperty("ReportedSymptom").GetString()??"";
            if(!symptom.Contains("vibration",StringComparison.OrdinalIgnoreCase) && !symptom.Contains("اهتزاز",StringComparison.Ordinal))
                return await Output("""{"outcome":"InsufficientEvidence"}""");
            if(!request.Messages.Any(m=>m.Role==LlmRole.Tool))
            {
                using var args=JsonDocument.Parse("""{"candidate":0,"query":"vibration pump seal"}""");
                return new ToolCompletionResponse("",[new("demo-retrieval","retrieve_evidence",args.RootElement)],new(10,5,15));
            }
            // No match without retrieved evidence. The actual agent validates and resolves e0.
            if(request.Messages.Last().Content=="[]")return await Output("""{"outcome":"InsufficientEvidence"}""");
            var description=system.Contains("descriptions in Arabic",StringComparison.Ordinal)
                ? "يشير الدليل إلى فحص اهتزاز المضخة وتسرب مانع التسرب. هذا شرح استشاري وليس اعتمادًا للسلامة."
                : "The manual supports checking pump vibration and seal leakage. This explanation is advisory, not safety approval.";
            return await Output(JsonSerializer.Serialize(new{outcome="Success",candidate=0,symptoms=new[]{new{description,evidence=new[]{"e0"}}}}));
        }
        if(system.Contains("You are the Diagnostic & Safety Planner",StringComparison.Ordinal))
            return await Output("""{"outcome":"Success","steps":[{"order":1,"instruction":"Check vibration","evidence":["e0"]}],"prerequisites":[]}""");
        if(system.Contains("You are the Work Order Generator",StringComparison.Ordinal))
            return await Output("""{"outcome":"Success","description":"Inspect isolated pump","actions":[{"order":1,"instruction":"Inspect seal","evidence":["e0"]}]}""");
        throw new NotSupportedException("Unknown demo role.");
    }
    private static Task<ToolCompletionResponse> Output(string text)=>Task.FromResult(new ToolCompletionResponse(text,[],new(10,10,20)));
    public Task<EmbeddingResult> GenerateEmbeddingsAsync(EmbeddingRequest request,CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        // Reproducible four-dimensional test space; does not claim learned semantic quality.
        return Task.FromResult(new EmbeddingResult(request.Inputs.Select(text=>(IReadOnlyList<float>)new float[]{
            text.Contains("vibration",StringComparison.OrdinalIgnoreCase)?1:0,
            text.Contains("seal",StringComparison.OrdinalIgnoreCase)?1:0,
            text.Contains("pump",StringComparison.OrdinalIgnoreCase)?1:0,0.1f}).ToArray(),"demo-test-v1"));
    }
    public Task<CompletionResponse> CompleteAsync(CompletionRequest request,CancellationToken ct)=>throw new NotSupportedException();
    public IAsyncEnumerable<StreamingChunk> StreamAsync(CompletionRequest request,CancellationToken ct)=>throw new NotSupportedException();
}
