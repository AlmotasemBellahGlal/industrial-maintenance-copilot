using IndustrialCopilot.Application.Reasoning;
using IndustrialCopilot.Application.Abstractions.AI.Models;
using IndustrialCopilot.Application.Abstractions.Tracing.Models;
using IndustrialCopilot.Domain.WorkOrders.Safety;

namespace IndustrialCopilot.Application.Tests.Reasoning;

public class LanguageInvariantTests
{
    [Fact]
    public async Task ArabicNarrativeProjectionRemainsWithinTheBoundedSseFrame()
    {
        var s=new Scenario();var description=new string('\u0636',4000);
        s.Llm.Tool();
        s.Llm.Output(System.Text.Json.JsonSerializer.Serialize(new {outcome="Success",candidate=0,
            symptoms=Enumerable.Range(0,8).Select(_=>new {description,evidence=new[]{"e0"}})},
            new System.Text.Json.JsonSerializerOptions { Encoder=System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
        s.Llm.Output(Scenario.Plan);s.Llm.Output(Scenario.Generated);
        var result=await s.Orchestrator().ExecuteAsync(s.Request,default);
        Assert.Equal(MaintenanceReasoningOutcome.Proposed,result.Outcome);
        Assert.Equal(description,result.Narrative);
        Assert.True(System.Text.Json.JsonSerializer.Serialize(result).Length<65536);
    }
    [Theory]
    [InlineData("en-US", "English")]
    [InlineData("ar-EG", "Arabic")]
    public async Task TrustedLanguageChangesNarrativeInstructionsButNeverScopeEvidenceOrSafety(string culture,string language)
    {
        var s=new Scenario();s.Success();
        var operations=s.Llm.Script.ToArray();s.Llm.Script.Clear();
        foreach(var operation in operations)
            s.Llm.Script.Enqueue((request,ct)=>
            {
                Assert.Contains(language,request.Messages[0].Content);
                Assert.Contains("Never translate citation snippets",request.Messages[0].Content);
                Assert.Equal(LlmRole.System,request.Messages[0].Role);
                return operation(request,ct);
            });
        var r=s.Request;
        var result=await s.Orchestrator().ExecuteAsync(new(r.ExecutionId,r.CorrelationId,r.RunId,r.WorkOrderId,r.Input,culture),default);
        Assert.Equal(MaintenanceReasoningOutcome.Proposed,result.Outcome);
        Assert.Equal("vibration",result.Narrative);
        var order=(await s.Store.GetWorkOrderAsync(r.WorkOrderId,default))!.Order;
        Assert.Equal("Inspect isolated pump",order.Content.Description);
        Assert.Equal("Inspect seal",Assert.Single(order.Content.Actions).Instruction);
        var prerequisite=Assert.Single(order.SafetyPrerequisites);
        Assert.Equal(s.Requirement.Id,prerequisite.Id);Assert.Equal(s.Requirement.Description,prerequisite.Description);
        Assert.True(prerequisite.IsMandatory);Assert.Equal(SafetyPrerequisiteStatus.Unverified,prerequisite.Status);
        Assert.Throws<InvalidOperationException>(()=>order.Dispatch(order.Revision));
        order.Approve("supervisor",order.Revision,DateTimeOffset.UtcNow);
        Assert.Throws<InvalidOperationException>(()=>order.Dispatch(order.Revision));
        order.VerifyPrerequisite(order.Revision,prerequisite.Id,new("verifier",DateTimeOffset.UtcNow,"simulated zero energy",true));
        order.Dispatch(order.Revision);
        var proposal=(await s.Store.GetProposalAsync(order.Id,default))!;
        var evidence=Assert.Single(proposal.Actions[0].Evidence);
        Assert.Equal(FakeRetrieval.Source,evidence.Snippet);Assert.Equal("page 7",evidence.Locator);Assert.Equal(s.Candidate.ManualRevisionId,evidence.ManualRevisionId);
        Assert.All(s.Trace.Current!.Steps.Where(step=>step.Kind==TraceOperationKind.Agent && step.Status==TraceStepStatus.Completed),step=>Assert.Null(step.Error));
    }
}
