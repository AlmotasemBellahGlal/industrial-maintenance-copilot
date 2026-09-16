using Npgsql;
namespace IndustrialCopilot.Infrastructure.Operations;

/// <summary>Equipment/manual association only. Upload never grants reviewed procedure or safety authority.</summary>
public sealed class ProductCatalog(NpgsqlDataSource source)
{
    public Task<bool> ContainsAsync(Guid equipment,Guid document,Guid revision,CancellationToken ct)=>OperationalSql.Run(source,async(c,t)=>{
        await using var cmd=OperationalSql.Command(c,t,"SELECT 1 FROM operations.manuals m JOIN operations.manual_revisions r ON r.manual_id=m.id WHERE m.equipment_id=@equipment AND m.id=@document AND r.id=@revision",("equipment",equipment),("document",document),("revision",revision));
        return await cmd.ExecuteScalarAsync(ct) is not null;
    },ct);
    public Task<bool> RegisterAsync(Guid equipment,Guid document,Guid revision,CancellationToken ct)=>OperationalSql.Run(source,async(c,t)=>{
        if(equipment==Guid.Empty || document==Guid.Empty || revision==Guid.Empty)throw new ArgumentException();
        // Equipment must already be provisioned by the trusted host. Never remap existing identities.
        await OperationalSql.Execute(c,t,"INSERT INTO operations.manuals(id,equipment_id) SELECT @document,id FROM operations.equipment WHERE id=@equipment ON CONFLICT DO NOTHING",ct,("equipment",equipment),("document",document));
        await OperationalSql.Execute(c,t,"INSERT INTO operations.manual_revisions(id,manual_id) SELECT @revision,id FROM operations.manuals WHERE id=@document AND equipment_id=@equipment ON CONFLICT DO NOTHING",ct,("equipment",equipment),("document",document),("revision",revision));
        await using var cmd=OperationalSql.Command(c,t,"SELECT 1 FROM operations.manuals m JOIN operations.manual_revisions r ON r.manual_id=m.id WHERE m.equipment_id=@equipment AND m.id=@document AND r.id=@revision",("equipment",equipment),("document",document),("revision",revision));
        return await cmd.ExecuteScalarAsync(ct) is not null;
    },ct);
}
