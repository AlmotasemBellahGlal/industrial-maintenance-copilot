using IndustrialCopilot.Application.Abstractions.Actions;
using IndustrialCopilot.Application.Abstractions.Approval.Models;
using IndustrialCopilot.Domain.WorkOrders.Safety;
using Npgsql;
using static IndustrialCopilot.Infrastructure.Operations.OperationalSql;

namespace IndustrialCopilot.Infrastructure.Operations;

public sealed class PostgresTrustedContext(NpgsqlDataSource source, IActionAuthorization authorization)
    : IEquipmentContextStore, ISafetyVerificationService
{
    public Task<EquipmentContext?> GetAsync(Guid equipmentId, CancellationToken ct)
    {
        if(equipmentId==Guid.Empty) throw new ArgumentException("Equipment identity required.");
        return Run(source,async(c,t)=>
        {
            await using var exists=Command(c,t,"SELECT id FROM operations.equipment WHERE id=@id",("id",equipmentId));
            if(await exists.ExecuteScalarAsync(ct) is not Guid) return null;
            var manuals=new List<ManualReference>();
            await using var command=Command(c,t,"SELECT m.id,r.id FROM operations.manuals m JOIN operations.manual_revisions r ON r.manual_id=m.id WHERE m.equipment_id=@id ORDER BY m.id,r.id",("id",equipmentId));
            await using var reader=await command.ExecuteReaderAsync(ct);
            while(await reader.ReadAsync(ct)) manuals.Add(new(reader.GetGuid(0),reader.GetGuid(1)));
            return new EquipmentContext(equipmentId,manuals.AsReadOnly());
        },ct);
    }
    public async Task<VerificationResult> RecordAsync(WorkOrderReviewTarget target,Guid prerequisiteId,string actorId,string evidence,bool satisfied,CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(target); ArgumentException.ThrowIfNullOrWhiteSpace(actorId);
        if(!await authorization.AuthorizeAsync(actorId,TrustedAction.VerifySafety,target.WorkOrderId,ct)) return new(DispatchGateOutcome.Forbidden,null);
        var verification=new SafetyVerification(actorId,DateTimeOffset.UtcNow,evidence,satisfied);
        return await Run(source,async(c,t)=>
        {
            var stored=await PostgresWorkflowStore.Load(c,t,target.WorkOrderId,true,ct);
            if(stored is null) return new(DispatchGateOutcome.NotFound,null);
            if(stored.Order.Revision!=target.Revision || stored.ConcurrencyToken!=target.ConcurrencyToken
                || await PostgresWorkflowStore.IsReserved(c,t,target.WorkOrderId,ct)) return new(DispatchGateOutcome.Conflict,null);
            try { stored.Order.VerifyPrerequisite(target.Revision,prerequisiteId,verification); }
            catch(InvalidOperationException) { return new(DispatchGateOutcome.NotDispatchable,null); }
            var token=await PostgresWorkflowStore.Save(c,t,stored.Order,stored.MaintenanceRunId,stored.ConcurrencyToken,ct);
            return new VerificationResult(token is null?DispatchGateOutcome.Conflict:DispatchGateOutcome.Ready,token);
        },ct);
    }
}
