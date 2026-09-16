using System.Text.Json;
using IndustrialCopilot.Application.Ask;
using Npgsql;
namespace IndustrialCopilot.Infrastructure.Operations;

public sealed class PostgresConversationStore(NpgsqlDataSource source) : IConversationStore
{
    public Task<Conversation> CreateAsync(string actor,Guid equipment,Guid document,Guid revision,string culture,CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        if(equipment==Guid.Empty || document==Guid.Empty || revision==Guid.Empty || culture is not ("en-US" or "ar-EG")) throw new ArgumentException();
        return OperationalSql.Run(source,async(c,t)=>{
            var id=Guid.NewGuid();
            await using var cmd=OperationalSql.Command(c,t,"""
                INSERT INTO operations.conversations(id,actor,equipment_id,document_id,revision_id,culture)
                VALUES(@id,@actor,@equipment,@document,@revision,@culture) RETURNING created_at,updated_at
                """,("id",id),("actor",actor),("equipment",equipment),("document",document),("revision",revision),("culture",culture));
            await using var reader=await cmd.ExecuteReaderAsync(ct); await reader.ReadAsync(ct);
            return new Conversation(id,equipment,document,revision,culture,reader.GetFieldValue<DateTimeOffset>(0),reader.GetFieldValue<DateTimeOffset>(1));
        },ct);
    }
    public Task<IReadOnlyList<Conversation>> ListAsync(string actor,IReadOnlyCollection<Guid> permittedEquipment,int offset,int limit,CancellationToken ct)
    {
        if(offset<0 || offset>10000 || limit is <1 or >50)throw new ArgumentOutOfRangeException(nameof(limit));
        ArgumentNullException.ThrowIfNull(permittedEquipment);
        return Read(actor,null,offset,limit,ct,permittedEquipment.ToArray());
    }
    public async Task<Conversation?> GetAsync(string actor,Guid id,CancellationToken ct)=>(await Read(actor,id,0,1,ct)).SingleOrDefault();
    private Task<IReadOnlyList<Conversation>> Read(string actor,Guid? id,int offset,int limit,CancellationToken ct,Guid[]? equipment=null)=>OperationalSql.Run<IReadOnlyList<Conversation>>(source,async(c,t)=>{
        await using var cmd=OperationalSql.Command(c,t,"""
            SELECT id,equipment_id,document_id,revision_id,culture,created_at,updated_at FROM operations.conversations
            WHERE actor=@actor AND (@id::uuid IS NULL OR id=@id) AND (@equipment::uuid[] IS NULL OR equipment_id=ANY(@equipment)) ORDER BY created_at DESC,id LIMIT @limit OFFSET @offset
            """,("actor",actor),("id",id),("limit",limit),("offset",offset),("equipment",equipment));
        var list=new List<Conversation>(); await using var rd=await cmd.ExecuteReaderAsync(ct);
        while(await rd.ReadAsync(ct))list.Add(new(rd.GetGuid(0),rd.GetGuid(1),rd.GetGuid(2),rd.GetGuid(3),rd.GetString(4),rd.GetFieldValue<DateTimeOffset>(5),rd.GetFieldValue<DateTimeOffset>(6)));
        return list.AsReadOnly();
    },ct);
    private static async Task<bool> Lock(NpgsqlConnection c,NpgsqlTransaction t,string actor,Guid id,CancellationToken ct)
    {
        await using var cmd=OperationalSql.Command(c,t,"SELECT id FROM operations.conversations WHERE id=@id AND actor=@actor FOR UPDATE",("id",id),("actor",actor));
        if(await cmd.ExecuteScalarAsync(ct) is not Guid)return false;
        await OperationalSql.Execute(c,t,"UPDATE operations.ask_turns SET state=4,finished_at=now() WHERE conversation_id=@id AND state=0 AND deadline<=now()",ct,("id",id));
        return true;
    }
    public Task<IReadOnlyList<AskTurn>> TurnsAsync(string actor,Guid id,long after,int limit,CancellationToken ct)
    {
        if(after<0 || limit is <1 or >50)throw new ArgumentOutOfRangeException(nameof(limit));
        return OperationalSql.Run<IReadOnlyList<AskTurn>>(source,async(c,t)=>{
            if(!await Lock(c,t,actor,id,ct))return Array.Empty<AskTurn>();
            await using var cmd=OperationalSql.Command(c,t,"SELECT id,sequence,question,answer,state,citations::text,correlation_id,created_at FROM operations.ask_turns WHERE conversation_id=@id AND sequence>@after ORDER BY sequence LIMIT @limit",("id",id),("after",after),("limit",limit));
            var list=new List<AskTurn>();await using var rd=await cmd.ExecuteReaderAsync(ct);
            while(await rd.ReadAsync(ct))list.Add(new(rd.GetGuid(0),rd.GetInt64(1),rd.GetString(2),rd.GetString(3),(AskState)rd.GetInt32(4),Array.AsReadOnly(JsonSerializer.Deserialize<AskCitation[]>(rd.GetString(5))!),rd.GetGuid(6),rd.GetFieldValue<DateTimeOffset>(7)));
            return list.AsReadOnly();
        },ct);
    }
    public Task<Guid?> BeginAsync(string actor,Guid id,string safeQuestion,Guid correlation,CancellationToken ct)=>OperationalSql.Run<Guid?>(source,async(c,t)=>{
        if(!await Lock(c,t,actor,id,ct))return null;
        var turn=Guid.NewGuid();
        await using var cmd=OperationalSql.Command(c,t,"""
            INSERT INTO operations.ask_turns(id,conversation_id,question,correlation_id)
            SELECT @turn,@id,@question,@correlation WHERE NOT EXISTS(SELECT 1 FROM operations.ask_turns WHERE conversation_id=@id AND state=0)
            RETURNING id
            """,("turn",turn),("id",id),("question",safeQuestion),("correlation",correlation));
        return await cmd.ExecuteScalarAsync(ct) is Guid result?result:null;
    },ct);
    public Task<bool> FinishAsync(string actor,Guid id,Guid turn,AskState state,string safeAnswer,IReadOnlyList<AskCitation> citations,CancellationToken ct)
    {
        if(!Enum.IsDefined(state)||state==AskState.Streaming)throw new ArgumentException();
        return OperationalSql.Run(source,async(c,t)=>{
            if(!await Lock(c,t,actor,id,ct))return false;
            var count=await OperationalSql.Execute(c,t,"""
                UPDATE operations.ask_turns SET state=@state,answer=@answer,citations=@citations::jsonb,finished_at=now()
                WHERE id=@turn AND conversation_id=@id AND state=0 AND deadline>now()
                """,ct,("state",(int)state),("answer",safeAnswer),("citations",JsonSerializer.Serialize(citations)),("turn",turn),("id",id));
            if(count==1)await OperationalSql.Execute(c,t,"UPDATE operations.conversations SET updated_at=now() WHERE id=@id",ct,("id",id));
            return count==1;
        },ct);
    }
}
