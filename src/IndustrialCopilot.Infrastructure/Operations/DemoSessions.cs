using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Npgsql;
using static IndustrialCopilot.Infrastructure.Operations.OperationalSql;

namespace IndustrialCopilot.Infrastructure.Operations;

public sealed record DemoSessionOptions(int MaxLive=200,int MaxPerClientPerHour=10,TimeSpan? Lifetime=null,int MaxStoredRows=20000)
{
    public TimeSpan SessionLifetime=>Lifetime??TimeSpan.FromHours(2);
    public static DemoSessionOptions From(IConfiguration config)
    {
        int Read(string key,int fallback,int min,int max)
        {
            var value=config["Demo:"+key] is {} text?int.Parse(text,System.Globalization.CultureInfo.InvariantCulture):fallback;
            if(value<min||value>max)throw new InvalidOperationException("Invalid demo session configuration.");
            return value;
        }
        return new(Read("MaxLiveSessions",200,1,2000),Read("MaxSessionsPerClientPerHour",10,1,100),TimeSpan.FromMinutes(Read("SessionMinutes",120,5,720)),Read("MaxStoredRows",20000,100,1000000));
    }
}
public enum DemoMintOutcome { Created, ClientLimit, CapacityFull }
public sealed record MintedDemoSession(string Actor,string Secret,DateTimeOffset ExpiresAt)
{ public override string ToString()=>"MintedDemoSession [redacted]"; }
public interface IDemoSessions
{
    Task<(DemoMintOutcome Outcome,MintedDemoSession? Session)> MintAsync(string clientKey,CancellationToken ct);
    /// <summary>The actor owning a live session secret, or null. Expired sessions never authenticate even before cleanup.</summary>
    Task<string?> AuthenticateAsync(string secret,CancellationToken ct);
    /// <summary>Deletes expired sessions and only the data they own. Returns the number of sessions removed.</summary>
    Task<int> PurgeExpiredAsync(int batch,CancellationToken ct);
}
/// <summary>
/// Durable anonymous sessions. Quotas are checked and the row inserted under one advisory lock, so concurrent
/// mints cannot overshoot them. Ownership is the actor id: cleanup deletes by that actor only and never touches
/// data with no visitor owner (seeded equipment, manuals, knowledge chunks).
/// </summary>
public sealed class PostgresDemoSessions(NpgsqlDataSource source,DemoSessionOptions options) : IDemoSessions
{
    public const string Prefix="visitor-";
    public const int SecretLength=47;
    private static byte[] Hash(string secret)=>SHA256.HashData(Encoding.UTF8.GetBytes(secret));
    public static bool LooksLikeSecret(string value)=>value.Length==SecretLength && value.StartsWith("dms_",StringComparison.Ordinal)
        && value.AsSpan(4).IndexOfAnyExcept("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_")<0;
    public static string ClientKey(string? address)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("demo-client\n"+address)))[..32];
    public Task<(DemoMintOutcome Outcome,MintedDemoSession? Session)> MintAsync(string clientKey,CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientKey);
        return Run(source,async(c,t)=>
        {
            await Execute(c,t,"SELECT pg_advisory_xact_lock(hashtextextended('demo-session-mint',0))",ct);
            await using(var counts=Command(c,t,
                "SELECT count(*) FILTER (WHERE expires_at>clock_timestamp()), count(*) FILTER (WHERE client_key=@client AND created_at>clock_timestamp()-interval '1 hour'),"+
                "(SELECT count(*) FROM operations.reasoning_jobs)+(SELECT count(*) FROM operations.conversations)+(SELECT count(*) FROM operations.ask_turns) FROM operations.demo_sessions",("client",clientKey)))
            await using(var r=await counts.ExecuteReaderAsync(ct))
            {
                await r.ReadAsync(ct);
                if(r.GetInt64(1)>=options.MaxPerClientPerHour) return (DemoMintOutcome.ClientLimit,null);
                if(r.GetInt64(0)>=options.MaxLive||r.GetInt64(2)>=options.MaxStoredRows) return (DemoMintOutcome.CapacityFull,null);
            }
            var actor=Prefix+Guid.NewGuid().ToString("N");
            var secret="dms_"+Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+','-').Replace('/','_');
            var expires=DateTimeOffset.UtcNow+options.SessionLifetime;
            await Execute(c,t,"INSERT INTO operations.demo_sessions(actor,secret_hash,client_key,expires_at) VALUES(@actor,@hash,@client,@expires)",ct,
                ("actor",actor),("hash",Hash(secret)),("client",clientKey),("expires",expires));
            return (DemoMintOutcome.Created,new MintedDemoSession(actor,secret,expires));
        },ct);
    }
    public Task<string?> AuthenticateAsync(string secret,CancellationToken ct)
    {
        if(!LooksLikeSecret(secret)) return Task.FromResult<string?>(null);
        return Run(source,async(c,t)=>
        {
            await using var cmd=Command(c,t,"SELECT actor FROM operations.demo_sessions WHERE secret_hash=@hash AND expires_at>clock_timestamp()",("hash",Hash(secret)));
            return await cmd.ExecuteScalarAsync(ct) as string;
        },ct);
    }
    public async Task<int> PurgeExpiredAsync(int batch,CancellationToken ct)
    {
        if(batch is <1 or >100) throw new ArgumentOutOfRangeException(nameof(batch));
        var actors=await Run(source,async(c,t)=>
        {
            var list=new List<string>();
            // Skip actors that still have queued/running jobs, a live ask turn, or any dispatch history.
            await using var cmd=Command(c,t,
                "SELECT s.actor FROM operations.demo_sessions s WHERE s.expires_at<=clock_timestamp()"+
                " AND NOT EXISTS(SELECT 1 FROM operations.reasoning_jobs j WHERE j.actor=s.actor AND j.status IN (1,2))"+
                " AND NOT EXISTS(SELECT 1 FROM operations.ask_turns a JOIN operations.conversations v ON v.id=a.conversation_id WHERE v.actor=s.actor AND a.state=0 AND a.deadline>clock_timestamp())"+
                " ORDER BY s.expires_at LIMIT @batch",("batch",batch));
            await using var r=await cmd.ExecuteReaderAsync(ct);
            while(await r.ReadAsync(ct)) list.Add(r.GetString(0));
            return list;
        },ct);
        var removed=0;
        foreach(var actor in actors) if(await PurgeAsync(actor,ct)) removed++;
        return removed;
    }
    private Task<bool> PurgeAsync(string actor,CancellationToken ct)=>Run(source,async(c,t)=>
    {
        // Same lock as submission and mint accounting so a purge cannot interleave with a new session count.
        await Execute(c,t,"SELECT pg_advisory_xact_lock(hashtextextended('demo-session-mint',0))",ct);
        await using(var still=Command(c,t,"SELECT 1 FROM operations.demo_sessions WHERE actor=@actor AND expires_at<=clock_timestamp() FOR UPDATE",("actor",actor)))
            if(await still.ExecuteScalarAsync(ct) is null) return false;
        const string Runs="SELECT run_id FROM operations.run_owners WHERE actor=@actor UNION SELECT run_id FROM operations.reasoning_jobs WHERE actor=@actor";
        const string Orders="SELECT id FROM operations.work_orders WHERE run_id IN ("+Runs+") UNION SELECT work_order_id FROM operations.reasoning_jobs WHERE actor=@actor";
        await using(var dispatched=Command(c,t,"SELECT 1 FROM operations.dispatch_attempts WHERE work_order_id IN ("+Orders+") OR run_id IN ("+Runs+") LIMIT 1",("actor",actor)))
            if(await dispatched.ExecuteScalarAsync(ct) is not null) return false;
        var a=("actor",(object?)actor);
        await Execute(c,t,"DELETE FROM operations.human_approvals WHERE id IN ("+Orders+")",ct,a);
        await Execute(c,t,"DELETE FROM operations.work_order_proposals WHERE id IN ("+Orders+")",ct,a);
        await Execute(c,t,"DELETE FROM operations.decisions WHERE id IN ("+Orders+")",ct,a);
        await Execute(c,t,"DELETE FROM operations.requirements WHERE id IN ("+Orders+")",ct,a);
        await Execute(c,t,"DELETE FROM operations.actions WHERE id IN ("+Orders+")",ct,a);
        await Execute(c,t,"DELETE FROM operations.traces WHERE run_id IN ("+Runs+")",ct,a);
        await Execute(c,t,"DELETE FROM operations.work_order_snapshots WHERE id IN ("+Orders+")",ct,a);
        await Execute(c,t,"DELETE FROM operations.work_orders WHERE id IN ("+Orders+")",ct,a);
        await Execute(c,t,"DELETE FROM operations.reasoning_job_events WHERE job_id IN (SELECT id FROM operations.reasoning_jobs WHERE actor=@actor)",ct,a);
        await Execute(c,t,"DELETE FROM operations.reasoning_job_attempts WHERE job_id IN (SELECT id FROM operations.reasoning_jobs WHERE actor=@actor)",ct,a);
        // Keep the run ids before the owner rows go; jobs reference runs, so jobs first, then runs.
        await Execute(c,t,"CREATE TEMP TABLE IF NOT EXISTS purge_runs(id uuid) ON COMMIT DROP",ct);
        await Execute(c,t,"INSERT INTO purge_runs "+Runs,ct,a);
        await Execute(c,t,"DELETE FROM operations.reasoning_jobs WHERE actor=@actor",ct,a);
        await Execute(c,t,"DELETE FROM operations.runs WHERE id IN (SELECT id FROM purge_runs)",ct,a);
        await Execute(c,t,"DELETE FROM operations.run_owners WHERE actor=@actor",ct,a);
        await Execute(c,t,"DELETE FROM operations.llm_usage WHERE actor=@actor",ct,a);
        await Execute(c,t,"DELETE FROM operations.ask_turns WHERE conversation_id IN (SELECT id FROM operations.conversations WHERE actor=@actor)",ct,a);
        await Execute(c,t,"DELETE FROM operations.conversations WHERE actor=@actor",ct,a);
        await Execute(c,t,"DELETE FROM operations.demo_sessions WHERE actor=@actor",ct,a);
        return true;
    },ct);
}

public sealed class DemoLimitException(string reason) : Exception("Demo limit reached: "+reason);
/// <summary>
/// Growth bounds enforced at the write itself, for visitor actors only (actor ids minted by demo sessions).
/// Every check runs under the same advisory lock as minting, so concurrent writes cannot overshoot, and an
/// existing session cannot bypass a limit that was fine when it was minted. Configured credentials are unaffected.
/// </summary>
internal static class DemoGrowth
{
    public const int MaxConversationsPerSession=20, MaxTurnsPerConversation=30, MaxTurnsPerSession=200;
    public static bool IsVisitor(string actor)=>actor.StartsWith(PostgresDemoSessions.Prefix,StringComparison.Ordinal);
    public static Task Lock(NpgsqlConnection c,NpgsqlTransaction t,CancellationToken ct)=>
        Execute(c,t,"SELECT pg_advisory_xact_lock(hashtextextended('demo-session-mint',0))",ct);
    /// <summary>Call after <see cref="Lock"/>. Throws DemoLimitException when the write would exceed a bound.</summary>
    public static async Task EnsureAsync(NpgsqlConnection c,NpgsqlTransaction t,string actor,Guid? conversation,string kind,DemoSessionOptions options,CancellationToken ct)
    {
        await using var cmd=Command(c,t,
            "SELECT (SELECT count(*) FROM operations.conversations WHERE actor=@actor),"+
            "(SELECT count(*) FROM operations.ask_turns k JOIN operations.conversations v ON v.id=k.conversation_id WHERE v.actor=@actor),"+
            "(SELECT count(*) FROM operations.ask_turns WHERE conversation_id=@conversation),"+
            "(SELECT count(*) FROM operations.reasoning_jobs)+(SELECT count(*) FROM operations.conversations)+(SELECT count(*) FROM operations.ask_turns)",
            ("actor",actor),("conversation",(object?)conversation??Guid.Empty));
        await using var r=await cmd.ExecuteReaderAsync(ct); await r.ReadAsync(ct);
        if(r.GetInt64(3)>=options.MaxStoredRows) throw new DemoLimitException("storage");
        if(kind=="conversation"&&r.GetInt64(0)>=MaxConversationsPerSession) throw new DemoLimitException("conversations");
        if(kind=="turn"&&(r.GetInt64(1)>=MaxTurnsPerSession||r.GetInt64(2)>=MaxTurnsPerConversation)) throw new DemoLimitException("turns");
    }
}
