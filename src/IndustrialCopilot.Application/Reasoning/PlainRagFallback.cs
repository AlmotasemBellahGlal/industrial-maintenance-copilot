using System.Text;
using System.Text.Json;
using IndustrialCopilot.Application.Abstractions.Agents.SymptomMatcher;
using IndustrialCopilot.Application.Ask;
namespace IndustrialCopilot.Application.Reasoning;

public sealed record GroundedFallbackResult(bool Answered,string? Answer,IReadOnlyList<AskCitation> Citations);
public interface IPlainRagFallback
{
    Task<GroundedFallbackResult> AnswerAsync(EquipmentManualCandidate candidate,string question,string culture,CancellationToken ct);
}
/// <summary>Reuses Ask grounding/refusal. No workflow storage, tools, policy mutation or dispatch capability.</summary>
public sealed class PlainRagFallback(AskService ask) : IPlainRagFallback
{
    public async Task<GroundedFallbackResult> AnswerAsync(EquipmentManualCandidate candidate,string question,string culture,CancellationToken ct)
    {
        var context=new Conversation(Guid.NewGuid(),candidate.EquipmentId,candidate.DocumentId,candidate.ManualRevisionId,culture,DateTimeOffset.UtcNow,DateTimeOffset.UtcNow);
        var answer=new StringBuilder(); IReadOnlyList<AskCitation> citations=[];var completed=false;
        await foreach(var item in ask.StreamAsync(context,new AskQuestion(question),ct))
        {
            if(item.Delta is {} delta)answer.Append(delta);
            if(item.Citations is {} evidence)citations=evidence;
            if(item.Type=="completed")completed=item.State==AskState.Completed;
        }
        ct.ThrowIfCancellationRequested();
        var result=new GroundedFallbackResult(completed,completed?answer.ToString():null,completed?citations:[]);
        // Keep the existing workflow SSE frame bound, including JSON escaping of Arabic and original excerpts.
        return JsonSerializer.Serialize(result).Length<=55000?result:new(false,null,[]);
    }
}
