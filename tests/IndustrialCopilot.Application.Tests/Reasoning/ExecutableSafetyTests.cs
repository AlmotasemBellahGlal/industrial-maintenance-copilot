using IndustrialCopilot.Domain.WorkOrders;
namespace IndustrialCopilot.Application.Tests.Reasoning;
public class ExecutableSafetyTests
{
    [Fact]
    public async Task ExactExecutableAssessmentUsesReviewedScopeAndCannotInheritDifferentProposalSafety()
    {
        var s=new Scenario();var content=new WorkOrderContent(s.Candidate.EquipmentId,s.Candidate.DocumentId,s.Candidate.ManualRevisionId,"noise","Inspect isolated pump",[new(1,"Inspect seal")]);
        var assessment=await s.Policy().AssessAsync(content,default);Assert.True(assessment.CanProceed);Assert.Equal(s.Requirement.Id,Assert.Single(assessment.Requirements).Id);Assert.Null(assessment.Requirements[0].Verification);
        var changed=new WorkOrderContent(content.EquipmentId,content.ManualId,content.ManualRevisionId,content.ReportedSymptom,content.Description,[new(1,"Operate live machinery")]);
        Assert.False((await s.Policy().AssessAsync(changed,default)).CanProceed);
        var revision=new WorkOrderContent(content.EquipmentId,content.ManualId,Guid.NewGuid(),content.ReportedSymptom,content.Description,content.Actions);
        Assert.False((await s.Policy().AssessAsync(revision,default)).CanProceed);
        using var cancelled=new CancellationTokenSource();cancelled.Cancel();await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>s.Policy().AssessAsync(content,cancelled.Token));
    }
}
