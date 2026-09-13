using System.Globalization;
using System.Security.Cryptography;
using IndustrialCopilot.Application.Abstractions.Actions;
using IndustrialCopilot.Application.Abstractions.Workflow;
using IndustrialCopilot.Domain.MaintenanceRuns;
using IndustrialCopilot.Domain.WorkOrders;
using Npgsql;
using static IndustrialCopilot.Infrastructure.Operations.OperationalSql;

namespace IndustrialCopilot.Infrastructure.Operations;

public sealed class PostgresDispatchAttemptStore(NpgsqlDataSource source,IActionAuthorization authorization) : IDispatchAttemptStore
{
    private IActionAuthorization Authorization => authorization;
    public async Task<DispatchReservation> ReserveAsync(DispatchCommand command,CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command); ct.ThrowIfCancellationRequested();
        if(!await authorization.AuthorizeAsync(command.ActorId,TrustedAction.Dispatch,command.Target.WorkOrderId,ct))
            return new(DispatchGateOutcome.Forbidden,null);
        return await Run(source,async(c,t)=>
        {
            var (stored,run)=await LockScope(c,t,command.Target.WorkOrderId,ct);
            if(stored is null) return new(DispatchGateOutcome.NotFound,null);
            await using var existing=Command(c,t,"SELECT attempt_id FROM operations.dispatch_attempts WHERE work_order_id=@id AND revision=@revision",
                ("id",stored.Order.Id),("revision",command.Target.Revision));
            if(await existing.ExecuteScalarAsync(ct) is Guid found)
            {
                var attempt=(await Load(c,t,found,ct))!;
                return command.Target.ConcurrencyToken==attempt.RequestedToken || command.Target.ConcurrencyToken==attempt.ReservedToken
                    ? new DispatchReservation(DispatchGateOutcome.Ready,attempt) : new(DispatchGateOutcome.Conflict,null);
            }
            if(stored.Order.Revision!=command.Target.Revision || stored.ConcurrencyToken!=command.Target.ConcurrencyToken)
                return new(DispatchGateOutcome.Conflict,null);
            if(!await Gate(c,t,stored,run,ct)) return new(DispatchGateOutcome.NotDispatchable,null);
            if(await RunReserved(c,t,stored.MaintenanceRunId,ct)) return new(DispatchGateOutcome.Conflict,null);
            if(await PostgresWorkflowStore.IsReserved(c,t,stored.Order.Id,ct)) return new(DispatchGateOutcome.Conflict,null);
            var token=await PostgresWorkflowStore.Save(c,t,stored.Order,stored.MaintenanceRunId,stored.ConcurrencyToken,ct);
            if(token is null) return new(DispatchGateOutcome.Conflict,null);
            var runToken=await BumpRun(c,t,run,ct);
            var id=Guid.NewGuid();
            await Execute(c,t,"""
                INSERT INTO operations.dispatch_attempts(attempt_id,work_order_id,revision,requested_token,reserved_token,run_id,run_token,actor_id,reserved_at,state)
                VALUES(@attempt,@id,@revision,@requested,@reserved,@run,@runToken,@actor,@at,1)
                """,ct,("attempt",id),("id",stored.Order.Id),("revision",stored.Order.Revision),
                ("requested",Guid.Parse(stored.ConcurrencyToken)),("reserved",Guid.Parse(token)),("run",stored.MaintenanceRunId),
                ("runToken",runToken),("actor",command.ActorId),("at",DateTimeOffset.UtcNow.ToString("O",CultureInfo.InvariantCulture)));
            await Event(c,t,id,command.ActorId,DispatchAttemptState.Pending,"reserved",ct);
            return new DispatchReservation(DispatchGateOutcome.Ready,await Load(c,t,id,ct));
        },ct);
    }
    public Task<DispatchAttempt?> GetAsync(Guid id,CancellationToken ct)
    {
        if(id==Guid.Empty) throw new ArgumentException("Attempt identity required.");
        return Run(source,(c,t)=>Load(c,t,id,ct),ct);
    }
    public async Task<DispatchAttemptSession?> OpenAsync(Guid id,CancellationToken ct)
    {
        if(id==Guid.Empty) throw new ArgumentException("Attempt identity required.");
        var key=BitConverter.ToInt64(SHA256.HashData(id.ToByteArray()),0);
        while(true)
        {
            ct.ThrowIfCancellationRequested();
            var connection=await source.OpenConnectionAsync(ct);
            try
            {
                await using var command=new NpgsqlCommand("SELECT pg_try_advisory_lock(@key)",connection);
                command.Parameters.AddWithValue("key",key);
                if(await command.ExecuteScalarAsync(ct) is true)
                {
                    await using var transaction=await connection.BeginTransactionAsync(ct);
                    var attempt=await Load(connection,transaction,id,ct);
                    await transaction.CommitAsync(ct);
                    if(attempt is not null) return new Session(this,connection,key,attempt);
                    await Unlock(connection,key); await connection.DisposeAsync(); return null;
                }
                // Waiting contenders never occupy the pool needed by the lock owner.
                await connection.DisposeAsync();
            }
            catch
            {
                // Cancellation after server execution may still own a session lock.
                NpgsqlConnection.ClearPool(connection); await connection.DisposeAsync();
                ct.ThrowIfCancellationRequested(); throw new OperationalStoreException();
            }
            await Task.Delay(TimeSpan.FromMilliseconds(25),ct);
        }
    }
    private static async Task Unlock(NpgsqlConnection connection,long key)
    {
        using var cleanup=new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var command=new NpgsqlCommand("SELECT pg_advisory_unlock(@key)",connection);
        command.Parameters.AddWithValue("key",key);
        await command.ExecuteNonQueryAsync(cleanup.Token);
    }
    private static async Task<(StoredWorkOrder?,StoredMaintenanceRun?)> LockScope(NpgsqlConnection c,NpgsqlTransaction t,Guid id,CancellationToken ct)
    {
        // All cross-aggregate operations lock run BEFORE work order (same order as publication).
        await using var command=Command(c,t,"SELECT run_id FROM operations.work_orders WHERE id=@id",("id",id));
        var value=await command.ExecuteScalarAsync(ct);
        StoredMaintenanceRun? run=null;
        if(value is Guid runId)
        {
            await using var row=Command(c,t,"SELECT equipment_id,symptom,status,cancellation_requested,token FROM operations.runs WHERE id=@id FOR UPDATE",("id",runId));
            await using var r=await row.ExecuteReaderAsync(ct);
            if(await r.ReadAsync(ct)) run=new(MaintenanceRun.Restore(runId,r.GetGuid(0),r.GetString(1),(MaintenanceRunStatus)r.GetInt32(2),r.GetBoolean(3)),r.GetGuid(4).ToString("D"));
        }
        return(await PostgresWorkflowStore.Load(c,t,id,true,ct),run);
    }
    private static async Task<bool> Gate(NpgsqlConnection c,NpgsqlTransaction t,StoredWorkOrder stored,StoredMaintenanceRun? run,CancellationToken ct)
    {
        var order=stored.Order;
        if(stored.MaintenanceRunId is not null && (run is null || run.Run.IsCancellationRequested
            || run.Run.Status is not (MaintenanceRunStatus.Running or MaintenanceRunStatus.WaitingForApproval))) return false;
        // A restored/imported approval is not proof that the human-review service recorded consent.
        await using var proof=Command(c,t,"SELECT token FROM operations.human_approvals WHERE id=@id AND revision=@revision",("id",order.Id),("revision",order.Revision));
        if(await proof.ExecuteScalarAsync(ct) is not Guid approvedToken) return false;
        var reviewed=await PostgresWorkflowStore.Load(c,t,order.Id,false,ct,approvedToken);
        if(reviewed is null || reviewed.Order.Content!=order.Content || reviewed.Order.ApprovalHistory.LastOrDefault()!=order.ApprovalHistory.LastOrDefault()
            || !reviewed.Order.SafetyPrerequisites.Select(p=>(p.Id,p.Description,p.IsMandatory)).SequenceEqual(order.SafetyPrerequisites.Select(p=>(p.Id,p.Description,p.IsMandatory)))) return false;
        try
        {
            var copy=WorkOrder.Restore(order.Id,order.Content,order.Revision,order.Status,order.SafetyAssessmentRevision,order.SafetyPrerequisites,order.ApprovalHistory);
            copy.Dispatch(copy.Revision); // Pure validation on detached copy; persistence remains Approved.
            return true;
        }
        catch(InvalidOperationException) { return false; }
    }
    private static async Task<Guid?> BumpRun(NpgsqlConnection c,NpgsqlTransaction t,StoredMaintenanceRun? run,CancellationToken ct)
    {
        if(run is null) return null;
        var next=Guid.NewGuid();
        await Execute(c,t,"UPDATE operations.runs SET token=@token WHERE id=@id",ct,("id",run.Run.Id),("token",next));
        return next;
    }
    private static async Task<bool> RunReserved(NpgsqlConnection c,NpgsqlTransaction t,Guid? runId,CancellationToken ct)
    {
        if(runId is null) return false;
        await using var command=Command(c,t,"SELECT EXISTS(SELECT 1 FROM operations.dispatch_attempts WHERE run_id=@id AND state IN (1,4))",("id",runId));
        return (bool)(await command.ExecuteScalarAsync(ct))!;
    }
    private static Task<int> Event(NpgsqlConnection c,NpgsqlTransaction t,Guid id,string actor,DispatchAttemptState state,string category,CancellationToken ct) =>
        Execute(c,t,"INSERT INTO operations.dispatch_events(attempt_id,actor_id,recorded_at,state,category) VALUES(@id,@actor,@at,@state,@category)",ct,
            ("id",id),("actor",actor),("at",DateTimeOffset.UtcNow.ToString("O",CultureInfo.InvariantCulture)),("state",(int)state),("category",category));

    private static async Task<DispatchAttempt?> Load(NpgsqlConnection c,NpgsqlTransaction t,Guid id,CancellationToken ct)
    {
        Guid work; int revision; string requested,reserved,actor,at; Guid? run; string? runToken,reference,category; DispatchAttemptState state; bool started;
        await using(var command=Command(c,t,"SELECT work_order_id,revision,requested_token,reserved_token,run_id,run_token,actor_id,reserved_at,state,invocation_started,external_reference,failure_category FROM operations.dispatch_attempts WHERE attempt_id=@id",("id",id)))
        await using(var r=await command.ExecuteReaderAsync(ct))
        {
            if(!await r.ReadAsync(ct)) return null;
            work=r.GetGuid(0); revision=r.GetInt32(1); requested=r.GetGuid(2).ToString("D"); reserved=r.GetGuid(3).ToString("D");
            run=r.IsDBNull(4)?null:r.GetGuid(4); runToken=r.IsDBNull(5)?null:r.GetGuid(5).ToString("D");
            actor=r.GetString(6); at=r.GetString(7); state=(DispatchAttemptState)r.GetInt32(8); started=r.GetBoolean(9);
            reference=r.IsDBNull(10)?null:r.GetString(10); category=r.IsDBNull(11)?null:r.GetString(11);
        }
        var snapshot=await PostgresWorkflowStore.Load(c,t,work,false,ct,Guid.Parse(reserved)) ?? throw new OperationalStoreException();
        return new(id,work,revision,requested,reserved,run,runToken,actor,DateTimeOffset.Parse(at,CultureInfo.InvariantCulture),state,started,reference,category,snapshot.Order.Content);
    }

    private sealed class Session(PostgresDispatchAttemptStore owner,NpgsqlConnection connection,long key,DispatchAttempt initial) : DispatchAttemptSession
    {
        private DispatchAttempt current=initial;
        public override DispatchAttempt Attempt=>current;
        private async Task<T> InTransaction<T>(Func<NpgsqlConnection,NpgsqlTransaction,Task<T>> action,CancellationToken ct)
        {
            try
            {
                await using var transaction=await connection.BeginTransactionAsync(ct);
                var result=await action(connection,transaction);
                await transaction.CommitAsync(ct);
                return result;
            }
            catch(Exception e) when(e is NpgsqlException or IOException or TimeoutException)
            { ct.ThrowIfCancellationRequested(); throw new OperationalStoreException(); }
        }
        private async Task Refresh(CancellationToken ct)=>current=(await InTransaction((c,t)=>Load(c,t,current.Id,ct),ct))!;
        public override async Task MarkInvocationStartedAsync(CancellationToken ct)
        {
            if(current.State!=DispatchAttemptState.Pending || current.InvocationStarted) throw new InvalidOperationException("Attempt cannot start another invocation.");
            await InTransaction(async(c,t)=>
            {
                await Execute(c,t,"UPDATE operations.dispatch_attempts SET invocation_started=true WHERE attempt_id=@id",ct,("id",current.Id));
                await Event(c,t,current.Id,current.ActorId,DispatchAttemptState.Pending,"invocation_started",ct);
                return true;
            },ct);
            await Refresh(ct);
        }
        public override async Task<bool> RestartAsync(string actor,CancellationToken ct)
        {
            if(current.State!=DispatchAttemptState.DefinitivelyFailed) return false;
            if(!await owner.Authorization.AuthorizeAsync(actor,TrustedAction.Dispatch,current.WorkOrderId,ct)) return false;
            var restarted=await InTransaction(async(c,t)=>
            {
                var (order,run)=await LockScope(c,t,current.WorkOrderId,ct);
                if(order is null || order.ConcurrencyToken!=current.ReservedToken || run?.ConcurrencyToken!=current.RunToken || !await Gate(c,t,order,run,ct)) return false;
                if(await RunReserved(c,t,order.MaintenanceRunId,ct)) return false;
                var token=await PostgresWorkflowStore.Save(c,t,order.Order,order.MaintenanceRunId,order.ConcurrencyToken,ct);
                if(token is null) return false;
                var runToken=await BumpRun(c,t,run,ct);
                await Execute(c,t,"UPDATE operations.dispatch_attempts SET state=1,invocation_started=false,reserved_token=@token,run_token=@run,failure_category=NULL WHERE attempt_id=@id",
                    ct,("token",Guid.Parse(token)),("run",runToken),("id",current.Id));
                await Event(c,t,current.Id,actor,DispatchAttemptState.Pending,"retry_reserved",ct);
                return true;
            },ct);
            await Refresh(ct);
            return restarted;
        }
        public override async Task RecordAsync(ExternalDispatchResult result,string actor,CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(result); ArgumentException.ThrowIfNullOrWhiteSpace(actor);
            if(current.State==DispatchAttemptState.Confirmed) return;
            await InTransaction(async(c,t)=>
            {
                var state=result.Outcome switch { ExternalDispatchOutcome.Accepted=>DispatchAttemptState.Confirmed,ExternalDispatchOutcome.DefinitivelyFailed=>DispatchAttemptState.DefinitivelyFailed,_=>DispatchAttemptState.Uncertain };
                var (order,run)=await LockScope(c,t,current.WorkOrderId,ct);
                if(order is null || order.ConcurrencyToken!=current.ReservedToken || run?.ConcurrencyToken!=current.RunToken) throw new InvalidOperationException("Reserved state changed.");
                if(state==DispatchAttemptState.Confirmed)
                {
                    if(!await Gate(c,t,order,run,ct)) throw new InvalidOperationException("Reserved dispatch gate no longer valid.");
                    order.Order.Dispatch(current.Revision);
                    if(await PostgresWorkflowStore.Save(c,t,order.Order,order.MaintenanceRunId,order.ConcurrencyToken,ct,true) is null) throw new InvalidOperationException("Confirmation conflict.");
                    if(run is not null)
                    {
                        if(run.Run.Status==MaintenanceRunStatus.WaitingForApproval) run.Run.Resume();
                        run.Run.Complete();
                        await Execute(c,t,"UPDATE operations.runs SET status=@status,token=@token WHERE id=@id",ct,("status",(int)run.Run.Status),("token",Guid.NewGuid()),("id",run.Run.Id));
                    }
                }
                await Execute(c,t,"UPDATE operations.dispatch_attempts SET state=@state,external_reference=@reference,failure_category=@category WHERE attempt_id=@id",ct,
                    ("state",(int)state),("reference",result.Reference),("category",state==DispatchAttemptState.Uncertain?"acceptance_unknown":state==DispatchAttemptState.DefinitivelyFailed?"not_accepted":null),("id",current.Id));
                await Event(c,t,current.Id,actor,state,result.Outcome.ToString(),ct);
                return true;
            },ct);
            await Refresh(ct);
        }
        public override async ValueTask DisposeAsync()
        {
            try { await Unlock(connection,key); }
            catch { NpgsqlConnection.ClearPool(connection); }
            finally { await connection.DisposeAsync(); }
        }
    }
}
