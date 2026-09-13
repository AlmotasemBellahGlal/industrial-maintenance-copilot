using System.Globalization;
using System.Text.Json;
using IndustrialCopilot.Application.Abstractions.Agents.WorkOrders;
using IndustrialCopilot.Application.Abstractions.Workflow;
using IndustrialCopilot.Domain.WorkOrders;
using IndustrialCopilot.Domain.WorkOrders.Safety;
using IndustrialCopilot.Domain.MaintenanceRuns;
using Npgsql;
using static IndustrialCopilot.Infrastructure.Operations.OperationalSql;

namespace IndustrialCopilot.Infrastructure.Operations;


/// <summary>Trusted persistence boundary. Callers authorize mutations before saving; no dispatch integration.</summary>
public sealed class PostgresWorkflowStore(NpgsqlDataSource source) : IWorkflowStore
{
    internal NpgsqlDataSource Source => source;

    private sealed class PublishConflict : Exception;

    public async Task<bool> TryPublishReviewAsync(WorkOrder order, MaintenanceRun run, string expectedRunToken, WorkOrderProposal proposal, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(order); ArgumentNullException.ThrowIfNull(run);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedRunToken);
        ArgumentNullException.ThrowIfNull(proposal);
        var copy = WorkOrder.Restore(order.Id, order.Content, order.Revision, order.Status, order.SafetyAssessmentRevision, order.SafetyPrerequisites, order.ApprovalHistory);
        if (copy.Status != WorkOrderStatus.PendingApproval || copy.ApprovalHistory.Count != 0 || run.Status != MaintenanceRunStatus.WaitingForApproval
            || run.IsCancellationRequested || run.EquipmentId != copy.Content.EquipmentId)
            throw new ArgumentException("Only unapproved review scope may be published for a waiting run.");
        var content = copy.Content;
        if (proposal.SelectedCandidate.EquipmentId != content.EquipmentId || proposal.SelectedCandidate.DocumentId != content.ManualId
            || proposal.SelectedCandidate.ManualRevisionId != content.ManualRevisionId || proposal.ReportedSymptom != content.ReportedSymptom
            || proposal.Description != content.Description || !proposal.Actions.Select(a => new WorkOrderAction(a.Order,a.Instruction)).SequenceEqual(content.Actions))
            throw new ArgumentException("Persisted scope must match the grounded proposal.");
        var runId = run.Id;
        try
        {
            return await Run(source, async(c,t) =>
            {
                var changed = await Execute(c,t,"UPDATE operations.runs SET status=3,token=@token WHERE id=@id AND token::text=@expected AND status=2 AND NOT cancellation_requested AND equipment_id=@equipment AND symptom=@symptom",ct,
                    ("id",runId),("token",Guid.NewGuid()),("expected",expectedRunToken),("equipment",copy.Content.EquipmentId),("symptom",copy.Content.ReportedSymptom));
                if (changed != 1) throw new PublishConflict();
                if (await Save(c,t,copy,runId,null,ct) is null) throw new PublishConflict();
                await Execute(c,t,"INSERT INTO operations.work_order_proposals(id,payload) VALUES(@id,CAST(@payload AS jsonb))",ct,
                    ("id",copy.Id),("payload",JsonSerializer.Serialize(proposal)));
                return true;
            },ct);
        }
        catch (PublishConflict) { return false; }
    }

    public Task<WorkOrderProposal?> GetProposalAsync(Guid workOrderId,CancellationToken ct)
    {
        if (workOrderId == Guid.Empty) throw new ArgumentException("Work order identity required.");
        return Run(source,async(c,t) =>
        {
            await using var cmd = Command(c,t,"SELECT payload::text FROM operations.work_order_proposals WHERE id=@id",("id",workOrderId));
            return await cmd.ExecuteScalarAsync(ct) is string json ? JsonSerializer.Deserialize<WorkOrderProposal>(json) : null;
        },ct);
    }

    public Task<StoredWorkOrder?> GetWorkOrderAsync(Guid id, CancellationToken ct)
    {
        if (id == Guid.Empty) throw new ArgumentException("Identity required.");
        return Run(source,(c,t) => Load(c,t,id,false,ct),ct);
    }

    public Task<string?> TrySaveWorkOrderAsync(WorkOrder order, Guid? runId, string? expectedToken, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(order);
        if (runId == Guid.Empty) throw new ArgumentException("Run identity required.");
        var copy = WorkOrder.Restore(order.Id,order.Content,order.Revision,order.Status,order.SafetyAssessmentRevision,order.SafetyPrerequisites,order.ApprovalHistory);
        return Run(source,async (c,t) =>
        {
            var current = await Load(c,t,copy.Id,true,ct);
            if (current?.ConcurrencyToken != expectedToken) return null;
            if (current is not null)
            {
                if (current.MaintenanceRunId != runId) throw new ArgumentException("Run association cannot change.");
                if (copy.Revision < current.Order.Revision || !copy.ApprovalHistory.Take(current.Order.ApprovalHistory.Count).SequenceEqual(current.Order.ApprovalHistory))
                    throw new ArgumentException("Revision and approval history cannot be rewritten.");
            }
            return await Save(c,t,copy,runId,expectedToken,ct);
        },ct);
    }

    internal static async Task<string?> Save(NpgsqlConnection c,NpgsqlTransaction t,WorkOrder order,Guid? runId,string? expected,CancellationToken ct)
    {
        var token = Guid.NewGuid();
        int changed;
        if (expected is null)
            changed = await Execute(c,t,"INSERT INTO operations.work_orders(id,token,run_id) VALUES(@id,@token,@run) ON CONFLICT(id) DO NOTHING",ct,
                ("id",order.Id),("token",token),("run",runId));
        else
            changed = await Execute(c,t,"UPDATE operations.work_orders SET token=@token WHERE id=@id AND token::text=@expected",ct,
                ("id",order.Id),("token",token),("expected",expected));
        if (changed == 0) return null;
        var content = order.Content;
        await Execute(c,t,"INSERT INTO operations.equipment(id) VALUES(@id) ON CONFLICT DO NOTHING",ct,("id",content.EquipmentId));
        await Execute(c,t,"INSERT INTO operations.manuals(id,equipment_id) VALUES(@id,@equipment) ON CONFLICT(id) DO NOTHING",ct,("id",content.ManualId),("equipment",content.EquipmentId));
        await Execute(c,t,"INSERT INTO operations.manual_revisions(id,manual_id) VALUES(@id,@manual) ON CONFLICT(id) DO NOTHING",ct,("id",content.ManualRevisionId),("manual",content.ManualId));
        if (runId is not null)
        {
            await using var run = Command(c,t,"SELECT equipment_id FROM operations.runs WHERE id=@id",("id",runId));
            if (await run.ExecuteScalarAsync(ct) is not Guid equipment || equipment != content.EquipmentId)
                throw new ArgumentException("Run and work-order equipment must match.");
        }
        await Execute(c,t,"""
            INSERT INTO operations.work_order_snapshots(id,token,revision,status,assessment_revision,equipment_id,manual_id,manual_revision_id,symptom,description)
            VALUES(@id,@token,@revision,@status,@assessment,@equipment,@manual,@manualrevision,@symptom,@description)
            """,ct,("id",order.Id),("token",token),("revision",order.Revision),("status",(int)order.Status),("assessment",order.SafetyAssessmentRevision),
            ("equipment",content.EquipmentId),("manual",content.ManualId),("manualrevision",content.ManualRevisionId),("symptom",content.ReportedSymptom),("description",content.Description));
        foreach (var action in content.Actions)
            await Execute(c,t,"INSERT INTO operations.actions VALUES(@id,@token,@order,@instruction)",ct,("id",order.Id),("token",token),("order",action.Order),("instruction",action.Instruction));
        for(var i=0;i<order.SafetyPrerequisites.Count;i++)
        {
            var p=order.SafetyPrerequisites[i]; var v=p.Verification;
            await Execute(c,t,"INSERT INTO operations.requirements VALUES(@id,@token,@position,@requirement,@description,@mandatory,@by,@at,@evidence,@satisfied)",ct,
                ("id",order.Id),("token",token),("position",i),("requirement",p.Id),("description",p.Description),("mandatory",p.IsMandatory),
                ("by",v?.VerifiedBy),("at",v?.VerifiedAt.ToString("O",CultureInfo.InvariantCulture)),("evidence",v?.Evidence),("satisfied",v?.IsSatisfied));
        }
        foreach(var d in order.ApprovalHistory)
            await Execute(c,t,"INSERT INTO operations.decisions VALUES(@id,@token,@revision,@kind,@supervisor,@at)",ct,
                ("id",order.Id),("token",token),("revision",d.Revision),("kind",(int)d.Kind),("supervisor",d.SupervisorId),("at",d.DecidedAt.ToString("O",CultureInfo.InvariantCulture)));
        return token.ToString("D");
    }

    internal static async Task<StoredWorkOrder?> Load(NpgsqlConnection c,NpgsqlTransaction t,Guid id,bool locked,CancellationToken ct)
    {
        Guid token; Guid? runId;
        await using(var head=Command(c,t,"SELECT token,run_id FROM operations.work_orders WHERE id=@id"+(locked?" FOR UPDATE":""),("id",id)))
        await using(var r=await head.ExecuteReaderAsync(ct))
        {
            if(!await r.ReadAsync(ct)) return null;
            token=r.GetGuid(0); runId=r.IsDBNull(1)?null:r.GetGuid(1);
        }
        int revision,status; int? assessment; Guid equipment,manual,manualRevision; string symptom,description;
        await using(var row=Command(c,t,"SELECT revision,status,assessment_revision,equipment_id,manual_id,manual_revision_id,symptom,description FROM operations.work_order_snapshots WHERE id=@id AND token=@token",("id",id),("token",token)))
        await using(var r=await row.ExecuteReaderAsync(ct))
        {
            if(!await r.ReadAsync(ct)) throw new OperationalStoreException();
            revision=r.GetInt32(0); status=r.GetInt32(1); assessment=r.IsDBNull(2)?null:r.GetInt32(2);
            equipment=r.GetGuid(3); manual=r.GetGuid(4); manualRevision=r.GetGuid(5); symptom=r.GetString(6); description=r.GetString(7);
        }
        var actions=new List<WorkOrderAction>();
        await using(var command=Command(c,t,"SELECT action_order,instruction FROM operations.actions WHERE id=@id AND token=@token ORDER BY action_order",("id",id),("token",token)))
        await using(var r=await command.ExecuteReaderAsync(ct))
            while(await r.ReadAsync(ct)) actions.Add(new(r.GetInt32(0),r.GetString(1)));
        var requirements=new List<SafetyPrerequisite>();
        await using(var command=Command(c,t,"SELECT requirement_id,description,mandatory,verified_by,verified_at,evidence,satisfied FROM operations.requirements WHERE id=@id AND token=@token ORDER BY position",("id",id),("token",token)))
        await using(var r=await command.ExecuteReaderAsync(ct))
            while(await r.ReadAsync(ct)) requirements.Add(SafetyPrerequisite.Restore(r.GetGuid(0),r.GetString(1),r.GetBoolean(2),
                r.IsDBNull(3)?null:new SafetyVerification(r.GetString(3),DateTimeOffset.Parse(r.GetString(4),CultureInfo.InvariantCulture),r.GetString(5),r.GetBoolean(6))));
        var decisions=new List<ApprovalDecision>();
        await using(var command=Command(c,t,"SELECT revision,kind,supervisor,decided_at FROM operations.decisions WHERE id=@id AND token=@token ORDER BY revision",("id",id),("token",token)))
        await using(var r=await command.ExecuteReaderAsync(ct))
            while(await r.ReadAsync(ct)) decisions.Add(ApprovalDecision.Restore(r.GetString(2),r.GetInt32(0),(ApprovalDecisionKind)r.GetInt32(1),DateTimeOffset.Parse(r.GetString(3),CultureInfo.InvariantCulture)));
        return new(WorkOrder.Restore(id,new(equipment,manual,manualRevision,symptom,description,actions),revision,(WorkOrderStatus)status,assessment,requirements,decisions),token.ToString("D"),runId);
    }

    public Task<StoredMaintenanceRun?> GetRunAsync(Guid id,CancellationToken ct)
    {
        if(id==Guid.Empty) throw new ArgumentException("Identity required.");
        return Run(source,async(c,t) =>
        {
            await using var cmd=Command(c,t,"SELECT equipment_id,symptom,status,cancellation_requested,token FROM operations.runs WHERE id=@id",("id",id));
            await using var r=await cmd.ExecuteReaderAsync(ct);
            if(!await r.ReadAsync(ct)) return null;
            return new StoredMaintenanceRun(MaintenanceRun.Restore(id,r.GetGuid(0),r.GetString(1),(MaintenanceRunStatus)r.GetInt32(2),r.GetBoolean(3)),r.GetGuid(4).ToString("D"));
        },ct);
    }

    public Task<string?> TrySaveRunAsync(MaintenanceRun run,string? expectedToken,CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(run);
        var copy=MaintenanceRun.Restore(run.Id,run.EquipmentId,run.ReportedSymptom,run.Status,run.IsCancellationRequested);
        return Run(source,async(c,t) =>
        {
            await Execute(c,t,"INSERT INTO operations.equipment(id) VALUES(@id) ON CONFLICT DO NOTHING",ct,("id",copy.EquipmentId));
            var token=Guid.NewGuid();
            var sql=expectedToken is null
                ? "INSERT INTO operations.runs VALUES(@id,@equipment,@symptom,@status,@cancel,@token) ON CONFLICT(id) DO NOTHING"
                : "UPDATE operations.runs SET status=@status,cancellation_requested=@cancel,token=@token WHERE id=@id AND token::text=@expected AND equipment_id=@equipment AND symptom=@symptom";
            var count=await Execute(c,t,sql,ct,("id",copy.Id),("equipment",copy.EquipmentId),("symptom",copy.ReportedSymptom),("status",(int)copy.Status),("cancel",copy.IsCancellationRequested),("token",token),("expected",expectedToken));
            return count==1?token.ToString("D"):null;
        },ct);
    }
}
