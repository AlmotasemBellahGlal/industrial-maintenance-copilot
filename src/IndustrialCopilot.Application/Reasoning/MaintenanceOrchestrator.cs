using IndustrialCopilot.Application.Abstractions.Jobs;
using IndustrialCopilot.Application.Abstractions.Agents;
using IndustrialCopilot.Application.Abstractions.Agents.DiagnosticPlanning;
using IndustrialCopilot.Application.Abstractions.Agents.WorkOrders;
using IndustrialCopilot.Application.Abstractions.AI;
using IndustrialCopilot.Application.Abstractions.Retrieval;
using IndustrialCopilot.Application.Abstractions.Safety;
using IndustrialCopilot.Application.Abstractions.Tracing;
using IndustrialCopilot.Application.Abstractions.Tracing.Models;
using IndustrialCopilot.Application.Abstractions.Workflow;
using IndustrialCopilot.Domain.MaintenanceRuns;
using IndustrialCopilot.Domain.WorkOrders;

namespace IndustrialCopilot.Application.Reasoning;

/// <summary>Sequential Pipeline. Host authorizes the request and supplies trusted candidates and policy configuration.</summary>
public sealed class MaintenanceOrchestrator
{
    private readonly ILlmProvider llm;
    private readonly IRetrievalService retrieval;
    private readonly IWorkflowStore workflows;
    private readonly ISafetyPolicy safety;
    private readonly IRunTraceStore traces;
    private readonly ReasoningLimits limits;
    private readonly TimeProvider clock;
    public MaintenanceOrchestrator(ILlmProvider llm,IRetrievalService retrieval,IWorkflowStore workflows,ISafetyPolicy safety,
        IRunTraceStore traces,ReasoningLimits? limits=null,TimeProvider? clock=null)
    {
        ArgumentNullException.ThrowIfNull(llm); ArgumentNullException.ThrowIfNull(retrieval); ArgumentNullException.ThrowIfNull(workflows);
        ArgumentNullException.ThrowIfNull(safety); ArgumentNullException.ThrowIfNull(traces);
        this.llm=llm; this.retrieval=retrieval; this.workflows=workflows; this.safety=safety; this.traces=traces;
        this.limits=limits??new(); this.clock=clock??TimeProvider.System;
    }

    public async Task<MaintenanceReasoningResult> ExecuteAsync(MaintenanceReasoningRequest request,CancellationToken cancellationToken,IProgress<MaintenanceProgress>? progress=null,
        bool durableAttempt=false,Func<MaintenanceProgress,CancellationToken,Task>? durableProgress=null)
    {
        ArgumentNullException.ThrowIfNull(request); cancellationToken.ThrowIfCancellationRequested();
        var input=request.Input;
        var run=new MaintenanceRun(request.RunId,input.Candidates[0].EquipmentId,input.ReportedSymptom);
        // Durable attempts replay only before publication, under the host persistence lease fence.
        if(await traces.GetAsync(request.ExecutionId,cancellationToken) is not null)
            return new(MaintenanceReasoningOutcome.Conflict,run.Id,null);
        string? runToken;
        if(durableAttempt)
        {
            var stored=await workflows.GetRunAsync(run.Id,cancellationToken);
            if(stored is null || stored.Run.EquipmentId!=run.EquipmentId || stored.Run.ReportedSymptom!=run.ReportedSymptom
                || stored.Run.Status is not (MaintenanceRunStatus.Queued or MaintenanceRunStatus.Running))
                return new(MaintenanceReasoningOutcome.Conflict,run.Id,null);
            run=stored.Run; runToken=stored.ConcurrencyToken;
        }
        else runToken=await workflows.TrySaveRunAsync(run,null,cancellationToken);
        if(runToken is null) return new(MaintenanceReasoningOutcome.Conflict,run.Id,null);
        var trace=new WorkflowTrace(traces,request.ExecutionId,request.CorrelationId,run.Id,clock);
        var root=trace.Start(TraceOperationKind.Orchestration,"maintenance_pipeline");
        var published=false;
        await Emit(MaintenanceProgressKind.WorkflowStarted);
        try
        {
            if(run.Status==MaintenanceRunStatus.Queued) run.Start();
            runToken=await workflows.TrySaveRunAsync(run,runToken,cancellationToken) ?? throw new WorkflowConflictException();
            await trace.Flush(cancellationToken);
            var match=await Stage(AgentRole.SymptomMatcher,(runtime,ct)=>new SymptomMatcherAgent(runtime,retrieval,limits).MatchAsync(input,ct),r=>r.Outcome);
            if(match.Outcome!=AgentOutcome.Success) return await Block(match.Outcome);
            var plan=await Stage(AgentRole.DiagnosticSafetyPlanner,(runtime,ct)=>new DiagnosticSafetyPlannerAgent(runtime).PlanAsync(new(input.ReportedSymptom,match.Match!),ct),r=>r.Outcome);
            if(plan.Outcome!=AgentOutcome.Success) return await Block(plan.Outcome);
            var assessment=await Assess(plan.Plan!,null);
            if(!assessment.CanProceed) return await Block(AgentOutcome.CannotProceed);
            var generated=await Stage(AgentRole.WorkOrderGenerator,(runtime,ct)=>new WorkOrderGeneratorAgent(runtime).GenerateAsync(new(input.ReportedSymptom,plan.Plan!),ct),r=>r.Outcome);
            if(generated.Outcome!=AgentOutcome.Success) return await Block(generated.Outcome);
            // Recheck EXACT final actions/description: a validated diagnostic plan cannot authorize a later expanded scope.
            assessment=await Assess(plan.Plan!,generated.Proposal!);
            if(!assessment.CanProceed) return await Block(AgentOutcome.CannotProceed);
            await CheckRun(cancellationToken);
            var proposal=generated.Proposal!; var candidate=proposal.SelectedCandidate;
            var order=new WorkOrder(request.WorkOrderId,new(candidate.EquipmentId,candidate.DocumentId,candidate.ManualRevisionId,
                input.ReportedSymptom,proposal.Description,proposal.Actions.Select(a=>new WorkOrderAction(a.Order,a.Instruction))));
            order.AssessSafety(order.Revision,assessment.Requirements);
            order.SubmitForApproval(order.Revision);
            run.WaitForApproval();
            if(!await workflows.TryPublishReviewAsync(order,run,runToken,proposal,cancellationToken)) throw new WorkflowConflictException();
            published=true;
            await Emit(MaintenanceProgressKind.WorkOrderReady,workOrder:order.Id);
            await Emit(MaintenanceProgressKind.WaitingForApproval,workOrder:order.Id);
            var review=trace.Start(TraceOperationKind.Approval,"human_review_boundary",root);
            trace.End(review,workOrder:order.Id,revision:order.Revision);
            trace.End(root);
            await trace.Flush(cancellationToken);
            // One grounded explanation (AgentJson.Text limits it to 4,000 characters).
            // Concatenating all symptoms could exceed the bounded SSE frame after JSON escaping.
            return new(MaintenanceReasoningOutcome.Proposed,run.Id,order.Id,Narrative:match.Match!.MatchedSymptoms[0].Description);
        }
        catch(OperationCanceledException) when(cancellationToken.IsCancellationRequested)
        {
            if(durableAttempt) throw; // Shutdown/lease loss must remain recoverable, not claim business cancellation.
            try { await Finish(true,"caller_cancelled"); }
            finally { cancellationToken.ThrowIfCancellationRequested(); }
            throw;
        }
        catch(DurableCancellationException)
        {
            if(!durableAttempt) await Finish(true,"durable_cancellation");
            return new(MaintenanceReasoningOutcome.Cancelled,run.Id,null);
        }
        catch(JobLeaseLostException) { throw; }
        catch(AgentTimedOutException)
        {
            var cancelled=await Finish(false,"agent_timeout");
            return new(cancelled?MaintenanceReasoningOutcome.Cancelled:MaintenanceReasoningOutcome.TimedOut,run.Id,null);
        }
        catch(WorkflowConflictException)
        {
            if(published) return new(MaintenanceReasoningOutcome.Proposed,run.Id,request.WorkOrderId,false);
            trace.End(root,TraceStepStatus.Failed,"workflow_conflict");
            using var cleanup=new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try { await trace.Flush(cleanup.Token); }
            catch { return new(MaintenanceReasoningOutcome.Conflict,run.Id,null,false); }
            return new(MaintenanceReasoningOutcome.Conflict,run.Id,null);
        }
        catch
        {
            if(published) return new(MaintenanceReasoningOutcome.Proposed,run.Id,request.WorkOrderId,false);
            var cancelled=await Finish(false,"dependency_failed");
            return new(cancelled?MaintenanceReasoningOutcome.Cancelled:MaintenanceReasoningOutcome.Failed,run.Id,null);
        }

        async Task CheckRun(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var current=await workflows.GetRunAsync(run.Id,ct) ?? throw new WorkflowConflictException();
            if(current.Run.IsCancellationRequested) throw new DurableCancellationException();
            if(current.ConcurrencyToken!=runToken) throw new WorkflowConflictException();
        }
        async Task<T> Stage<T>(AgentRole role,Func<AgentRuntime,CancellationToken,Task<T>> operation,Func<T,AgentOutcome> getOutcome)
        {
            await CheckRun(cancellationToken);
            await Emit(MaintenanceProgressKind.AgentStarted,role);
            var id=trace.Start(TraceOperationKind.Agent,role.ToString(),root,role);
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(limits.AgentTimeout);
            try
            {
                var result=await operation(new(llm,limits,trace,id,request.ResponseCulture),timeout.Token).WaitAsync(timeout.Token);
                timeout.Token.ThrowIfCancellationRequested();
                var outcome=getOutcome(result);
                await Emit(MaintenanceProgressKind.AgentCompleted,role);
                trace.End(id,outcome==AgentOutcome.Success?TraceStepStatus.Completed:TraceStepStatus.Failed,
                    outcome==AgentOutcome.Success?null:outcome==AgentOutcome.InsufficientEvidence?"insufficient_evidence":"cannot_proceed");
                await trace.Flush(cancellationToken);
                return result;
            }
            catch(OperationCanceledException) when(timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            { trace.End(id,TraceStepStatus.Cancelled); throw new AgentTimedOutException(); }
            catch(OperationCanceledException) { trace.End(id,TraceStepStatus.Cancelled); throw; }
            catch { trace.End(id,TraceStepStatus.Failed,"agent_failed"); throw; }
        }
        async Task<SafetyAssessment> Assess(DiagnosticPlan plan,WorkOrderProposal? proposal)
        {
            await CheckRun(cancellationToken);
            var id=trace.Start(TraceOperationKind.Tool,"deterministic_safety_policy",root);
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(limits.AgentTimeout);
            try
            {
                var result=await safety.AssessAsync(plan,proposal,timeout.Token).WaitAsync(timeout.Token);
                timeout.Token.ThrowIfCancellationRequested();
                await Emit(MaintenanceProgressKind.SafetyEvaluated,allowed:result.CanProceed);
                trace.End(id,result.CanProceed?TraceStepStatus.Completed:TraceStepStatus.Failed,result.CanProceed?null:"safety_blocked");
                await trace.Flush(cancellationToken);
                return result;
            }
            catch(OperationCanceledException) when(timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            { trace.End(id,TraceStepStatus.Cancelled); throw new AgentTimedOutException(); }
            catch(OperationCanceledException) { trace.End(id,TraceStepStatus.Cancelled); throw; }
            catch { trace.End(id,TraceStepStatus.Failed,"safety_policy_failed"); throw; }
        }
        async Task<MaintenanceReasoningResult> Block(AgentOutcome outcome)
        {
            await CheckRun(cancellationToken);
            run.Block();
            runToken=await workflows.TrySaveRunAsync(run,runToken,cancellationToken) ?? throw new WorkflowConflictException();
            trace.End(root,TraceStepStatus.Failed,outcome==AgentOutcome.InsufficientEvidence?"insufficient_evidence":"cannot_proceed");
            await trace.Flush(cancellationToken);
            await Emit(MaintenanceProgressKind.Blocked);
            return new(outcome==AgentOutcome.InsufficientEvidence?MaintenanceReasoningOutcome.InsufficientEvidence:MaintenanceReasoningOutcome.CannotProceed,run.Id,null);
        }
        async Task<bool> Finish(bool cancelled,string code)
        {
            using var cleanup=new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var current=await workflows.GetRunAsync(run.Id,cleanup.Token);
            cancelled=cancelled || current?.Run.IsCancellationRequested==true;
            if(current is not null && current.Run.Status is MaintenanceRunStatus.Queued or MaintenanceRunStatus.Running
                && (current.ConcurrencyToken==runToken || current.Run.IsCancellationRequested))
            {
                if(cancelled || current.Run.IsCancellationRequested)
                {
                    if(!current.Run.IsCancellationRequested) current.Run.RequestCancellation();
                    current.Run.AcknowledgeCancellation();
                }
                else if(current.Run.Status==MaintenanceRunStatus.Running) current.Run.Fail();
                if(await workflows.TrySaveRunAsync(current.Run,current.ConcurrencyToken,cleanup.Token) is null) throw new WorkflowConflictException();
            }
            trace.End(root,cancelled?TraceStepStatus.Cancelled:TraceStepStatus.Failed,code);
            await trace.Flush(cleanup.Token);
            await Emit(cancelled?MaintenanceProgressKind.Cancelled:MaintenanceProgressKind.Failed);
            return cancelled;
        }
        async Task Emit(MaintenanceProgressKind kind,AgentRole? role=null,bool? allowed=null,Guid? workOrder=null)
        {
            var value=new MaintenanceProgress(kind,run.Id,role,allowed,workOrder);
            if(durableProgress is not null) await durableProgress(value,cancellationToken);
            try { progress?.Report(value); } catch { /* Legacy observation is not workflow authority. */ }
        }
    }
    private sealed class DurableCancellationException : OperationCanceledException;
}
