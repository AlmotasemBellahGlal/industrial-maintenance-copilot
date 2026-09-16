using System.Text.Json;
using IndustrialCopilot.Application.Abstractions.Usage;
using IndustrialCopilot.Application.Abstractions.Tracing.Models;
using Npgsql;
namespace IndustrialCopilot.Infrastructure.Operations;

public sealed class PostgresLlmUsageStore(NpgsqlDataSource source) : ILlmUsageStore
{
    private static void Validate(LlmUsageRecord r,bool terminal)
    {
        if(r.CallId==Guid.Empty || r.Context.CorrelationId==Guid.Empty || r.Context.EquipmentId==Guid.Empty
            || r.Context.RunId==Guid.Empty || r.Context.ExecutionId==Guid.Empty || r.Context.StepId==Guid.Empty
            || string.IsNullOrWhiteSpace(r.Context.Actor) || r.Context.Actor.Length>200
            || string.IsNullOrWhiteSpace(r.Provider) || r.Provider.Length>64 || string.IsNullOrWhiteSpace(r.Model) || r.Model.Length>200
            || !Enum.IsDefined(r.Status) || !Enum.IsDefined(r.Operation) || !Enum.IsDefined(r.Billing) || !Enum.IsDefined(r.Context.Purpose)
            || r.StartedAt==default || terminal!=(r.Status!=UsageStatus.Started) || terminal!=r.CompletedAt.HasValue || r.CompletedAt<r.StartedAt
            || (!terminal && (r.Tokens is not null || r.EstimatedCost is not null))
            || (r.Tokens is {} u && (long)u.PromptTokens+u.CompletionTokens!=u.TotalTokens)
            || (r.EstimatedCost is not null && (r.Billing!=BillingKind.Hosted || r.Tokens is null || string.IsNullOrWhiteSpace(r.PricingVersion))))
            throw new ArgumentException("Invalid usage observation.");
    }
    public Task BeginAsync(LlmUsageRecord record,CancellationToken ct)
    {
        Validate(record,false);
        return OperationalSql.Run(source,async(c,t)=>{
            var json=JsonSerializer.Serialize(record);
            await OperationalSql.Execute(c,t,"""
                INSERT INTO operations.llm_usage(call_id,actor,equipment_id,correlation_id,run_id,provider,started_at,status,payload)
                VALUES(@id,@actor,@equipment,@correlation,@run,@provider,@started,0,@payload::jsonb) ON CONFLICT DO NOTHING
                """,ct,("id",record.CallId),("actor",record.Context.Actor),("equipment",record.Context.EquipmentId),
                ("correlation",record.Context.CorrelationId),("run",record.Context.RunId),("provider",record.Provider),("started",record.StartedAt),("payload",json));
            await using var check=OperationalSql.Command(c,t,"SELECT payload=@payload::jsonb FROM operations.llm_usage WHERE call_id=@id",("id",record.CallId),("payload",json));
            if(await check.ExecuteScalarAsync(ct) is not true)throw new InvalidOperationException("Usage identity conflict.");
            return true;
        },ct);
    }
    public Task FinishAsync(LlmUsageRecord record,CancellationToken ct)
    {
        Validate(record,true);
        return OperationalSql.Run(source,async(c,t)=>{
            var json=JsonSerializer.Serialize(record);
            var count=await OperationalSql.Execute(c,t,"""
                UPDATE operations.llm_usage SET status=@status,payload=@payload::jsonb
                WHERE call_id=@id AND status=0 AND payload->'Context'=(@payload::jsonb)->'Context'
                AND payload->'Model'=(@payload::jsonb)->'Model' AND payload->'Provider'=(@payload::jsonb)->'Provider'
                AND payload->'Operation'=(@payload::jsonb)->'Operation' AND payload->'Billing'=(@payload::jsonb)->'Billing'
                AND payload->'StartedAt'=(@payload::jsonb)->'StartedAt'
                """,ct,("id",record.CallId),("status",(int)record.Status),("payload",json));
            if(count==0)
            {
                await using var check=OperationalSql.Command(c,t,"SELECT payload=@payload::jsonb FROM operations.llm_usage WHERE call_id=@id",("id",record.CallId),("payload",json));
                if(await check.ExecuteScalarAsync(ct) is not true)throw new InvalidOperationException("Usage finalization conflict.");
            }
            return true;
        },ct);
    }
    public Task<UsagePage> QueryAsync(string actor,IReadOnlyCollection<Guid> equipment,UsageQuery query,CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);ArgumentNullException.ThrowIfNull(equipment);query.Validate();
        var ids=equipment.ToArray();
        return OperationalSql.Run(source,async(c,t)=>{
            await OperationalSql.Execute(c,t,"SET TRANSACTION ISOLATION LEVEL REPEATABLE READ",ct);
            const string filter="""
                FROM operations.llm_usage WHERE actor=@actor AND equipment_id=ANY(@equipment)
                AND (@correlation::uuid IS NULL OR correlation_id=@correlation) AND (@run::uuid IS NULL OR run_id=@run)
                AND (@provider::text IS NULL OR provider=@provider) AND (@from::timestamptz IS NULL OR started_at>=@from)
                AND (@until::timestamptz IS NULL OR started_at<@until)
                """;
            (string,object?)[] args=[("actor",actor),("equipment",ids),("correlation",query.CorrelationId),("run",query.RunId),
                ("provider",query.Provider),("from",query.From),("until",query.Until),("offset",query.Offset),("limit",query.Limit)];
            var rows=new List<LlmUsageRecord>();
            await using(var cmd=OperationalSql.Command(c,t,"SELECT payload::text "+filter+" ORDER BY started_at DESC,call_id LIMIT @limit OFFSET @offset",args))
            await using(var rd=await cmd.ExecuteReaderAsync(ct))
                while(await rd.ReadAsync(ct))rows.Add(JsonSerializer.Deserialize<LlmUsageRecord>(rd.GetString(0))!);
            long calls,known,unpriced,nonHosted;long? prompt,completion,total;
            await using(var cmd=OperationalSql.Command(c,t,"""
                SELECT count(*),count(payload->'Tokens'->>'TotalTokens'),
                sum((payload->'Tokens'->>'PromptTokens')::bigint),sum((payload->'Tokens'->>'CompletionTokens')::bigint),sum((payload->'Tokens'->>'TotalTokens')::bigint),
                count(*) FILTER(WHERE (payload->>'Billing')::int=0 AND payload->'EstimatedCost'->>'Amount' IS NULL),
                count(*) FILTER(WHERE (payload->>'Billing')::int<>0)
                """+filter,args))
            await using(var rd=await cmd.ExecuteReaderAsync(ct))
            {
                await rd.ReadAsync(ct);calls=rd.GetInt64(0);known=rd.GetInt64(1);
                long? Sum(int i)=>rd.IsDBNull(i)?null:checked((long)rd.GetDecimal(i));
                prompt=Sum(2);completion=Sum(3);total=Sum(4);unpriced=rd.GetInt64(5);nonHosted=rd.GetInt64(6);
            }
            var costs=new List<MonetaryCost>();
            await using(var cmd=OperationalSql.Command(c,t,"SELECT payload->'EstimatedCost'->>'Currency',sum((payload->'EstimatedCost'->>'Amount')::numeric) "+filter+" AND payload->'EstimatedCost'->>'Amount' IS NOT NULL GROUP BY 1 ORDER BY 1",args))
            await using(var rd=await cmd.ExecuteReaderAsync(ct))
                while(await rd.ReadAsync(ct))costs.Add(new(rd.GetDecimal(1),rd.GetString(0)));
            return new UsagePage(rows.AsReadOnly(),new(calls,known,prompt,completion,total,calls-known,unpriced,nonHosted,costs.AsReadOnly()));
        },ct);
    }
}
