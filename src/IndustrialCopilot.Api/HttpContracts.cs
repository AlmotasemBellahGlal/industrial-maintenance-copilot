using IndustrialCopilot.Application.Abstractions.Approval.Models;
using IndustrialCopilot.Domain.WorkOrders;
using IndustrialCopilot.Domain.WorkOrders.Safety;
namespace IndustrialCopilot.Api;

public sealed record StartRunRequest(Guid EquipmentId,string Symptom);
public sealed record TargetRequest(int Revision,string ConcurrencyToken)
{
    public WorkOrderReviewTarget Bind(Guid id) { HttpValidation.Id(id); if(Revision<1) throw new ArgumentException(); HttpValidation.Text(ConcurrencyToken,100); return new(id,Revision,ConcurrencyToken); }
}
public sealed record ActionRequest(int Order,string Instruction);
public sealed record ContentRequest(Guid EquipmentId,Guid ManualId,Guid ManualRevisionId,string ReportedSymptom,string Description,IReadOnlyList<ActionRequest> Actions)
{
    public WorkOrderContent Bind()
    {
        HttpValidation.Text(ReportedSymptom,2000); HttpValidation.Text(Description,4000);
        if(Actions is null || Actions.Count is <1 or >32 || Actions.Any(a=>a is null)) throw new ArgumentException();
        foreach(var a in Actions) HttpValidation.Text(a.Instruction,2000);
        return new(EquipmentId,ManualId,ManualRevisionId,ReportedSymptom,Description,Actions.Select(a=>new WorkOrderAction(a.Order,a.Instruction)));
    }
    public static ContentRequest From(WorkOrderContent c)=>new(c.EquipmentId,c.ManualId,c.ManualRevisionId,c.ReportedSymptom,c.Description,c.Actions.Select(a=>new ActionRequest(a.Order,a.Instruction)).ToArray());
}
public sealed record RequirementRequest(Guid Id,string Description,bool Mandatory)
{
    public SafetyPrerequisite Bind(){HttpValidation.Text(Description,2000);return new(Id,Description,Mandatory);}
}
public sealed record DecisionRequest(TargetRequest Target,string Decision,ContentRequest? EditedContent=null,IReadOnlyList<RequirementRequest>? ReviewedRequirements=null);
public sealed record VerificationRequest(TargetRequest Target,Guid PrerequisiteId,string Evidence,bool Satisfied);
public sealed record RequirementResponse(Guid Id,string Description,bool Mandatory,string Status,string? VerifiedBy,string? Evidence);
public sealed record ReviewResponse(Guid WorkOrderId,TargetRequest Target,ContentRequest Content,string Status,int? SafetyAssessmentRevision,IReadOnlyList<RequirementResponse> Requirements,string? Decision)
{
    public static ReviewResponse From(WorkOrderReviewSnapshot s)=>new(s.Target.WorkOrderId,new(s.Target.Revision,s.Target.ConcurrencyToken),ContentRequest.From(s.Content),s.Status.ToString(),s.SafetyAssessmentRevision,
        s.SafetyPrerequisites.Select(p=>new RequirementResponse(p.Id,p.Description,p.IsMandatory,p.Status.ToString(),p.Verification?.VerifiedBy,p.Verification?.Evidence)).ToArray(),s.LatestDecision?.Kind.ToString());
}
public sealed record RunResponse(Guid RunId,Guid EquipmentId,string Symptom,string Status,bool CancellationRequested,IReadOnlyList<Guid> WorkOrderIds,IReadOnlyList<Guid> ExecutionIds);
public sealed record WorkflowResponse(Guid RunId,Guid? WorkOrderId,Guid ExecutionId,Guid CorrelationId,string Outcome,string? Narrative=null);
public sealed record DispatchResponse(Guid? AttemptId,Guid? WorkOrderId,int? Revision,string Outcome,string? State,string? ExternalReference,string? Failure);
public sealed record TraceStepResponse(string Kind,string Name,string Status,string? Error,DateTimeOffset StartedAt,DateTimeOffset? CompletedAt);
public sealed record TraceResponse(Guid ExecutionId,Guid CorrelationId,Guid? RunId,IReadOnlyList<TraceStepResponse> Steps);
public sealed class ApiProblemException(int status,string code) : Exception(code) {public int Status {get;}=status; public string Code {get;}=code;}
public static class HttpValidation
{
    public static void Id(Guid id){if(id==Guid.Empty) throw new ArgumentException("Identity required.");}
    public static void Text(string value,int max){if(string.IsNullOrWhiteSpace(value)||value.Length>max) throw new ArgumentException("Invalid text.");}
}
