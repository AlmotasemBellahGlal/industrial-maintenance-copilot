using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IndustrialCopilot.Application.Abstractions.Jobs;
using IndustrialCopilot.Application.Abstractions.Agents;
using IndustrialCopilot.Application.Abstractions.Agents.SymptomMatcher;
using IndustrialCopilot.Application.Reasoning;
using IndustrialCopilot.Domain.MaintenanceRuns;
using Npgsql;
using static IndustrialCopilot.Infrastructure.Operations.OperationalSql;

namespace IndustrialCopilot.Infrastructure.Operations;

/// <summary>Short transactions only; no connection or database lock is held across model calls.</summary>
public sealed class PostgresReasoningJobStore(NpgsqlDataSource source) : IReasoningJobStore
{
    public Task<ReasoningJobSnapshot> SubmitAsync(ReasoningJobSubmission submission,CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(submission);
        var r=submission.Request;
        var input=new SymptomMatchInput(r.Input.ReportedSymptom.Trim(),r.Input.Candidates.OrderBy(c=>c.EquipmentId).ThenBy(c=>c.DocumentId).ThenBy(c=>c.ManualRevisionId).ToArray(),r.Input.InitialEvidence);
        var normalized=new MaintenanceReasoningRequest(r.ExecutionId,r.CorrelationId,r.RunId,r.WorkOrderId,input,r.ResponseCulture);
        var hash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new {input,normalized.ResponseCulture}))));
        return Run(source,async(c,t)=>
        {
            // Hash collisions only serialize unrelated submissions; exact unique key/hash checks decide identity.
            await Execute(c,t,"SELECT pg_advisory_xact_lock(hashtextextended(@key,0))",ct,("key",submission.ActorId+"\n"+submission.IdempotencyKey));
            await using(var existing=Command(c,t,"SELECT id,request_hash FROM operations.reasoning_jobs WHERE actor=@actor AND submission_key=@key",("actor",submission.ActorId),("key",submission.IdempotencyKey)))
            {
                Guid? existingId=null;
                await using(var reader=await existing.ExecuteReaderAsync(ct))
                    if(await reader.ReadAsync(ct)) { if(reader.GetString(1)!=hash) throw new JobSubmissionConflictException(); existingId=reader.GetGuid(0); }
                if(existingId.HasValue) return (await Load(c,t,existingId.Value,ct))!;
            }
            var id=Guid.NewGuid();var equipment=input.Candidates[0].EquipmentId;
            await Execute(c,t,"INSERT INTO operations.runs(id,equipment_id,symptom,status,cancellation_requested,token) VALUES(@run,@equipment,@symptom,1,false,@token)",ct,
                ("run",r.RunId),("equipment",equipment),("symptom",input.ReportedSymptom),("token",Guid.NewGuid()));
            await Execute(c,t,"INSERT INTO operations.reasoning_jobs(id,actor,submission_key,request_hash,run_id,work_order_id,equipment_id,correlation_id,payload) VALUES(@id,@actor,@key,@hash,@run,@order,@equipment,@correlation,CAST(@payload AS jsonb))",ct,
                ("id",id),("actor",submission.ActorId),("key",submission.IdempotencyKey),("hash",hash),("run",r.RunId),("order",r.WorkOrderId),("equipment",equipment),("correlation",r.CorrelationId),("payload",JsonSerializer.Serialize(normalized)));
            return (await Load(c,t,id,ct))!;
        },ct);
    }
    public Task<ReasoningJobSnapshot?> GetAsync(Guid id,CancellationToken ct)
    {
        if(id==Guid.Empty) throw new ArgumentException("Job identity required.");
        return Run(source,(c,t)=>Load(c,t,id,ct),ct);
    }
    private static async Task<ReasoningJobSnapshot?> Load(NpgsqlConnection c,NpgsqlTransaction t,Guid id,CancellationToken ct)
    {
        ReasoningJobSnapshot value;
        await using(var cmd=Command(c,t,"SELECT run_id,equipment_id,correlation_id,status,phase,created_at,updated_at,cancellation_requested,version,result::text,failure_code FROM operations.reasoning_jobs WHERE id=@id FOR SHARE",("id",id)))
        await using(var r=await cmd.ExecuteReaderAsync(ct))
        {
            if(!await r.ReadAsync(ct)) return null;
            value=new(id,r.GetGuid(0),r.GetGuid(1),r.GetGuid(2),(ReasoningJobStatus)r.GetInt32(3),(ReasoningJobPhase)r.GetInt32(4),r.GetFieldValue<DateTimeOffset>(5),r.GetFieldValue<DateTimeOffset>(6),r.GetBoolean(7),r.GetInt64(8),[],r.IsDBNull(9)?null:JsonSerializer.Deserialize<MaintenanceReasoningResult>(r.GetString(9)),r.IsDBNull(10)?null:r.GetString(10));
        }
        var attempts=new List<ReasoningJobAttempt>();
        await using(var cmd=Command(c,t,"SELECT attempt,execution_id,started_at,ended_at,failure_code FROM operations.reasoning_job_attempts WHERE job_id=@id ORDER BY attempt",("id",id)))
        await using(var r=await cmd.ExecuteReaderAsync(ct))
            while(await r.ReadAsync(ct)) attempts.Add(new(r.GetInt32(0),r.GetGuid(1),r.GetFieldValue<DateTimeOffset>(2),r.IsDBNull(3)?null:r.GetFieldValue<DateTimeOffset>(3),r.IsDBNull(4)?null:r.GetString(4)));
        return value with {Attempts=attempts.AsReadOnly()};
    }
    public Task<ReasoningJobSnapshot?> RequestCancellationAsync(Guid id,CancellationToken ct)=>Run(source,async(c,t)=>
    {
        await Execute(c,t,"SELECT id FROM operations.reasoning_jobs WHERE id=@id FOR UPDATE",ct,("id",id));
        var current=await Load(c,t,id,ct);
        if(current is null) return null;
        if(current.Status==ReasoningJobStatus.Cancelled) return current;
        if(current.IsTerminal) throw new JobCancellationConflictException();
        var run=await RunState(c,t,current.MaintenanceRunId,ct);
        // Publication wins if it committed first. Cancelling processing is not cancelling approved business work.
        if(run.Status is not (MaintenanceRunStatus.Queued or MaintenanceRunStatus.Running)) throw new JobCancellationConflictException();
        if(!run.IsCancellationRequested) run.RequestCancellation();
        if(current.Status==ReasoningJobStatus.Queued) run.AcknowledgeCancellation();
        await SaveRun(c,t,run,ct);
        await Execute(c,t,"UPDATE operations.reasoning_jobs SET cancellation_requested=true,status=CASE WHEN status=1 THEN 5 ELSE status END,phase=CASE WHEN status=1 THEN 9 ELSE phase END,version=version+1,updated_at=clock_timestamp() WHERE id=@id",ct,("id",id));
        if(current.Status==ReasoningJobStatus.Queued)
            await Terminal(c,t,id,ReasoningJobStatus.Cancelled,ReasoningJobPhase.Cancelled,new(MaintenanceReasoningOutcome.Cancelled,run.Id,null,TraceComplete:false),"cancelled",ct);
        return await Load(c,t,id,ct);
    },ct);

    public Task<ReasoningJobClaim?> ClaimAsync(string owner,IReadOnlyCollection<Guid> equipmentIds,TimeSpan lease,CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);ValidateLease(lease);
        ArgumentNullException.ThrowIfNull(equipmentIds);
        if(owner.Length>100 || equipmentIds.Count==0 || equipmentIds.Contains(Guid.Empty)) throw new ArgumentException("Worker identity/scope required.");
        return Run<ReasoningJobClaim?>(source,async(c,t)=>
        {
            Guid id; string actor,payload;int attempt;
            await using(var cmd=Command(c,t,"""
                SELECT id,actor,payload::text,attempt FROM operations.reasoning_jobs
                WHERE equipment_id=ANY(@equipment) AND ((status=1 AND available_at<=clock_timestamp()) OR (status=2 AND lease_until<=clock_timestamp()))
                ORDER BY available_at,created_at,id LIMIT 1 FOR UPDATE SKIP LOCKED
                """,("equipment",equipmentIds.ToArray())))
            await using(var r=await cmd.ExecuteReaderAsync(ct))
            {
                if(!await r.ReadAsync(ct)) return null;
                id=r.GetGuid(0);actor=r.GetString(1);payload=r.GetString(2);attempt=r.GetInt32(3);
            }
            var request=JsonSerializer.Deserialize<MaintenanceReasoningRequest>(payload)??throw new OperationalStoreException();
            var job=(await Load(c,t,id,ct))!;
            await EndAttempt(c,t,id,"interrupted",ct);
            if(await FinalizeExisting(c,t,job,request.WorkOrderId,null,ct)) return null;
            if(attempt>=3)
            {
                await FailRun(c,t,request.RunId,ct);
                await Terminal(c,t,id,ReasoningJobStatus.Failed,ReasoningJobPhase.Failed,new(MaintenanceReasoningOutcome.Failed,request.RunId,null,TraceComplete:false),"recovery_exhausted",ct);return null;
            }
            var execution=attempt==0?request.ExecutionId:Guid.NewGuid();var leaseToken=Guid.NewGuid();
            await Execute(c,t,"UPDATE operations.reasoning_jobs SET status=2,phase=CASE WHEN attempt=0 THEN 1 ELSE 10 END,owner=@owner,lease_token=@token,lease_until=clock_timestamp()+@lease,attempt=attempt+1,version=version+1,updated_at=clock_timestamp(),failure_code=NULL WHERE id=@id",ct,
                ("id",id),("owner",owner),("token",leaseToken),("lease",lease));
            await Execute(c,t,"INSERT INTO operations.reasoning_job_attempts(job_id,attempt,execution_id) VALUES(@id,@attempt,@execution)",ct,("id",id),("attempt",attempt+1),("execution",execution));
            return new(id,leaseToken,owner,attempt+1,actor,new(execution,request.CorrelationId,request.RunId,request.WorkOrderId,request.Input,request.ResponseCulture));
        },ct);
    }
    public Task<JobLeaseState> RenewAsync(ReasoningJobClaim claim,TimeSpan lease,CancellationToken ct)
    {
        ValidateLease(lease);
        return Run(source,async(c,t)=>
        {
            if(!await Owned(c,t,claim,ct)) return JobLeaseState.Lost;
            var current=(await Load(c,t,claim.JobId,ct))!;
            if(current.CancellationRequested) return JobLeaseState.CancellationRequested;
            await Execute(c,t,"UPDATE operations.reasoning_jobs SET lease_until=clock_timestamp()+@lease WHERE id=@id",ct,("id",claim.JobId),("lease",lease));
            return JobLeaseState.Owned;
        },ct);
    }
    public Task<IReadOnlyList<ReasoningJobEvent>> ReadProgressAsync(Guid id,long after,int limit,CancellationToken ct)
    {
        if(id==Guid.Empty || after<0 || limit is <1 or >128) throw new ArgumentException("Invalid progress range.");
        return Run<IReadOnlyList<ReasoningJobEvent>>(source,async(c,t)=>
        {
            var events=new List<ReasoningJobEvent>();
            await using var cmd=Command(c,t,"SELECT sequence,execution_id,payload::text FROM operations.reasoning_job_events WHERE job_id=@id AND sequence>@after ORDER BY sequence LIMIT @limit",("id",id),("after",after),("limit",limit));
            await using var r=await cmd.ExecuteReaderAsync(ct);
            while(await r.ReadAsync(ct)) events.Add(new(r.GetInt64(0),r.GetGuid(1),JsonSerializer.Deserialize<MaintenanceProgress>(r.GetString(2))!));
            return events.AsReadOnly();
        },ct);
    }
    public Task ReportAsync(ReasoningJobClaim claim,MaintenanceProgress progress,CancellationToken ct)=>Run(source,async(c,t)=>
    {
        if(progress.RunId!=claim.Request.RunId || !await Owned(c,t,claim,ct)) throw new JobLeaseLostException();
        var phase=progress.Kind switch {
            MaintenanceProgressKind.WorkflowStarted=>ReasoningJobPhase.Starting,
            MaintenanceProgressKind.AgentStarted=>progress.Role switch {AgentRole.SymptomMatcher=>ReasoningJobPhase.MatchingSymptoms,AgentRole.DiagnosticSafetyPlanner=>ReasoningJobPhase.PlanningDiagnostics,_=>ReasoningJobPhase.GeneratingWorkOrder},
            MaintenanceProgressKind.SafetyEvaluated=>ReasoningJobPhase.ValidatingSafety,
            MaintenanceProgressKind.WaitingForApproval or MaintenanceProgressKind.WorkOrderReady=>ReasoningJobPhase.WaitingForApproval,
            MaintenanceProgressKind.Blocked=>ReasoningJobPhase.Blocked,
            MaintenanceProgressKind.Failed=>ReasoningJobPhase.Failed,
            MaintenanceProgressKind.Cancelled=>ReasoningJobPhase.Cancelled,
            _=>(await Load(c,t,claim.JobId,ct))!.Phase};
        var changed=await Execute(c,t,"UPDATE operations.reasoning_jobs SET phase=@phase,version=version+1,updated_at=clock_timestamp() WHERE id=@id AND NOT cancellation_requested",ct,("id",claim.JobId),("phase",(int)phase));
        if(changed!=1) throw new JobLeaseLostException();
        await Execute(c,t,"INSERT INTO operations.reasoning_job_events(job_id,sequence,execution_id,payload) SELECT id,version,@execution,CAST(@payload AS jsonb) FROM operations.reasoning_jobs WHERE id=@id",ct,("id",claim.JobId),("execution",claim.Request.ExecutionId),("payload",JsonSerializer.Serialize(progress)));
        return true;
    },ct);
    public Task CompleteAsync(ReasoningJobClaim claim,MaintenanceReasoningResult? result,CancellationToken ct)=>Run(source,async(c,t)=>
    {
        if(!await Owned(c,t,claim,ct)) throw new JobLeaseLostException();
        var job=(await Load(c,t,claim.JobId,ct))!;
        if(await FinalizeExisting(c,t,job,claim.Request.WorkOrderId,result,ct)) return true;
        await FailRun(c,t,claim.Request.RunId,ct);
        await Terminal(c,t,job.JobId,ReasoningJobStatus.Failed,ReasoningJobPhase.Failed,result,"reasoning_failed",ct);
        return true;
    },ct);
    public Task ReleaseAsync(ReasoningJobClaim claim,CancellationToken ct)=>Run(source,async(c,t)=>
    {
        if(!await Owned(c,t,claim,ct)) return false;
        var job=(await Load(c,t,claim.JobId,ct))!;
        if(await FinalizeExisting(c,t,job,claim.Request.WorkOrderId,null,ct)) return true;
        await EndAttempt(c,t,claim.JobId,"worker_interrupted",ct);
        await Execute(c,t,"UPDATE operations.reasoning_jobs SET status=1,phase=10,owner=NULL,lease_token=NULL,lease_until=NULL,available_at=clock_timestamp()+@delay,version=version+1,updated_at=clock_timestamp(),failure_code='worker_interrupted' WHERE id=@id",ct,
            ("id",claim.JobId),("delay",TimeSpan.FromSeconds(Math.Pow(2,claim.Attempt))));
        return true;
    },ct);
    private static async Task<bool> Owned(NpgsqlConnection c,NpgsqlTransaction t,ReasoningJobClaim claim,CancellationToken ct)
    {
        await using var cmd=Command(c,t,"SELECT 1 FROM operations.reasoning_jobs WHERE id=@id AND lease_token=@token AND owner=@owner AND status=2 AND lease_until>clock_timestamp() FOR UPDATE",("id",claim.JobId),("token",claim.LeaseToken),("owner",claim.Owner));
        return await cmd.ExecuteScalarAsync(ct) is not null;
    }
    private static async Task<bool> FinalizeExisting(NpgsqlConnection c,NpgsqlTransaction t,ReasoningJobSnapshot job,Guid order,MaintenanceReasoningResult? result,CancellationToken ct)
    {
        var run=await RunState(c,t,job.MaintenanceRunId,ct);
        await using var published=Command(c,t,"SELECT 1 FROM operations.work_orders WHERE id=@id AND run_id=@run",("id",order),("run",run.Id));
        if(await published.ExecuteScalarAsync(ct) is not null)
        {
            await Terminal(c,t,job.JobId,ReasoningJobStatus.Succeeded,ReasoningJobPhase.WaitingForApproval,
                result?.Outcome==MaintenanceReasoningOutcome.Proposed?result:new(MaintenanceReasoningOutcome.Proposed,run.Id,order,TraceComplete:false),null,ct);return true;
        }
        if(job.CancellationRequested || run.IsCancellationRequested)
        {
            if(run.Status is MaintenanceRunStatus.Queued or MaintenanceRunStatus.Running)
            {
                if(!run.IsCancellationRequested)run.RequestCancellation();run.AcknowledgeCancellation();await SaveRun(c,t,run,ct);
            }
            await Execute(c,t,"UPDATE operations.reasoning_jobs SET cancellation_requested=true WHERE id=@id",ct,("id",job.JobId));
            await Terminal(c,t,job.JobId,ReasoningJobStatus.Cancelled,ReasoningJobPhase.Cancelled,new(MaintenanceReasoningOutcome.Cancelled,run.Id,null,TraceComplete:false),"cancelled",ct);return true;
        }
        if(run.Status is MaintenanceRunStatus.Blocked or MaintenanceRunStatus.Failed)
        {
            await Terminal(c,t,job.JobId,ReasoningJobStatus.Failed,run.Status==MaintenanceRunStatus.Blocked?ReasoningJobPhase.Blocked:ReasoningJobPhase.Failed,result??new(run.Status==MaintenanceRunStatus.Blocked?MaintenanceReasoningOutcome.CannotProceed:MaintenanceReasoningOutcome.Failed,run.Id,null,TraceComplete:false),run.Status==MaintenanceRunStatus.Blocked?"reasoning_blocked":"reasoning_failed",ct);return true;
        }
        return false;
    }
    private static async Task Terminal(NpgsqlConnection c,NpgsqlTransaction t,Guid id,ReasoningJobStatus status,ReasoningJobPhase phase,MaintenanceReasoningResult? result,string? failure,CancellationToken ct)
    {
        await Execute(c,t,"UPDATE operations.reasoning_jobs SET status=@status,phase=@phase,owner=NULL,lease_token=NULL,lease_until=NULL,result=CAST(@result AS jsonb),failure_code=@failure,version=version+1,updated_at=clock_timestamp() WHERE id=@id",ct,
            ("id",id),("status",(int)status),("phase",(int)phase),("result",result is null?null:JsonSerializer.Serialize(result)),("failure",failure));
        await EndAttempt(c,t,id,failure,ct);
    }
    private static Task<int> EndAttempt(NpgsqlConnection c,NpgsqlTransaction t,Guid id,string? failure,CancellationToken ct)=>Execute(c,t,"UPDATE operations.reasoning_job_attempts SET ended_at=clock_timestamp(),failure_code=@failure WHERE job_id=@id AND ended_at IS NULL",ct,("id",id),("failure",failure));
    private static async Task<MaintenanceRun> RunState(NpgsqlConnection c,NpgsqlTransaction t,Guid id,CancellationToken ct)
    {
        await using var cmd=Command(c,t,"SELECT equipment_id,symptom,status,cancellation_requested FROM operations.runs WHERE id=@id FOR UPDATE",("id",id));
        await using var r=await cmd.ExecuteReaderAsync(ct);if(!await r.ReadAsync(ct))throw new OperationalStoreException();
        return MaintenanceRun.Restore(id,r.GetGuid(0),r.GetString(1),(MaintenanceRunStatus)r.GetInt32(2),r.GetBoolean(3));
    }
    private static Task<int> SaveRun(NpgsqlConnection c,NpgsqlTransaction t,MaintenanceRun run,CancellationToken ct)=>Execute(c,t,"UPDATE operations.runs SET status=@status,cancellation_requested=@cancel,token=@token WHERE id=@id",ct,("id",run.Id),("status",(int)run.Status),("cancel",run.IsCancellationRequested),("token",Guid.NewGuid()));
    private static async Task FailRun(NpgsqlConnection c,NpgsqlTransaction t,Guid id,CancellationToken ct)
    {
        var run=await RunState(c,t,id,ct);
        if(run.Status==MaintenanceRunStatus.Queued)run.Start();
        if(run.Status==MaintenanceRunStatus.Running){run.Fail();await SaveRun(c,t,run,ct);}
    }
    private static void ValidateLease(TimeSpan lease)
    {if(lease<TimeSpan.FromSeconds(1) || lease>TimeSpan.FromMinutes(5))throw new ArgumentOutOfRangeException(nameof(lease));}
}
