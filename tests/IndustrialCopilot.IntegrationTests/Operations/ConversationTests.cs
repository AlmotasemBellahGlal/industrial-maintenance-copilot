using IndustrialCopilot.Application.Ask;
using IndustrialCopilot.Infrastructure.Operations;
using IndustrialCopilot.IntegrationTests.Knowledge;
namespace IndustrialCopilot.IntegrationTests.Operations;
public class ConversationTests(KnowledgeDatabase database):IClassFixture<KnowledgeDatabase>
{
    private async Task<Conversation> Create()
    {
        await new OperationalSchema(database.Source).ApplyAsync(default);
        var equipment=Guid.NewGuid();await using var cmd=database.Source.CreateCommand("INSERT INTO operations.equipment VALUES(@id)");cmd.Parameters.AddWithValue("id",equipment);await cmd.ExecuteNonQueryAsync();
        var document=Guid.NewGuid();var revision=Guid.NewGuid();Assert.True(await new ProductCatalog(database.Source).RegisterAsync(equipment,document,revision,default));
        return await new PostgresConversationStore(database.Source).CreateAsync("owner",equipment,document,revision,"ar-EG",default);
    }
    [PostgresFact]public async Task ConcurrentTurnsSerializeAndFinalizationIsOwnerCheckedAndSingleUse()
    {
        var c=await Create();var s=new PostgresConversationStore(database.Source);
        var starts=await Task.WhenAll(Enumerable.Range(0,4).Select(_=>s.BeginAsync("owner",c.Id,"pump",Guid.NewGuid(),default)));
        var turn=Assert.Single(starts,id=>id.HasValue)!.Value;
        Assert.False(await s.FinishAsync("other",c.Id,turn,AskState.Completed,"leak",[],default));
        var citation=new AskCitation(c.DocumentId,c.ManualRevisionId,Guid.NewGuid(),"page 2","pump");
        Assert.True(await s.FinishAsync("owner",c.Id,turn,AskState.Completed,"answer",[citation],default));
        Assert.False(await s.FinishAsync("owner",c.Id,turn,AskState.Failed,"overwrite",[],default));
        // New repository instance simulates no process-local history dependency.
        var loaded=new PostgresConversationStore(database.Source);var history=await loaded.TurnsAsync("owner",c.Id,0,20,default);
        Assert.Equal(citation,Assert.Single(Assert.Single(history).Citations));Assert.Equal("answer",history[0].Answer);
        Assert.Null(await loaded.GetAsync("other",c.Id,default));Assert.Empty(await loaded.TurnsAsync("other",c.Id,0,20,default));Assert.Empty(await loaded.ListAsync("other",[c.EquipmentId],0,20,default));
        Assert.Equal("ar-EG",(await loaded.GetAsync("owner",c.Id,default))!.Culture);
    }
    [PostgresFact]public async Task ExpiredRequestBecomesFailedAndCannotPublishLateAnswer()
    {
        var c=await Create();var s=new PostgresConversationStore(database.Source);var turn=(await s.BeginAsync("owner",c.Id,"pump",Guid.NewGuid(),default))!.Value;
        await using var cmd=database.Source.CreateCommand("UPDATE operations.ask_turns SET deadline=now()-interval '1 second' WHERE id=@id");cmd.Parameters.AddWithValue("id",turn);await cmd.ExecuteNonQueryAsync();
        Assert.Equal(AskState.Failed,Assert.Single(await s.TurnsAsync("owner",c.Id,0,20,default)).State);
        Assert.False(await s.FinishAsync("owner",c.Id,turn,AskState.Completed,"late",[],default));
        Assert.NotNull(await s.BeginAsync("owner",c.Id,"new",Guid.NewGuid(),default));
    }
    [PostgresFact]public async Task CancelledFailedAndRefusedTurnsRemainOrderedAndPaginated()
    {
        var c=await Create();var s=new PostgresConversationStore(database.Source);
        foreach(var state in new[]{AskState.Cancelled,AskState.Failed,AskState.InsufficientEvidence}){var turn=(await s.BeginAsync("owner",c.Id,"pump",Guid.NewGuid(),default))!.Value;Assert.True(await s.FinishAsync("owner",c.Id,turn,state,"",[],default));}
        var first=Assert.Single(await s.TurnsAsync("owner",c.Id,0,1,default));Assert.Equal(AskState.Cancelled,first.State);
        var rest=await s.TurnsAsync("owner",c.Id,first.Sequence,2,default);Assert.Equal(new[]{AskState.Failed,AskState.InsufficientEvidence},rest.Select(t=>t.State));Assert.True(rest[0].Sequence<rest[1].Sequence);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async()=>await s.ListAsync("owner",[c.EquipmentId],0,51,default));
    }
    [PostgresFact]public async Task UploadCannotRemapManualOrRevisionToAnotherEquipment()
    {
        var c=await Create();var catalog=new ProductCatalog(database.Source);
        Assert.True(await catalog.RegisterAsync(c.EquipmentId,c.DocumentId,c.ManualRevisionId,default));
        Assert.False(await catalog.RegisterAsync(Guid.NewGuid(),c.DocumentId,c.ManualRevisionId,default));
        Assert.True(await catalog.ContainsAsync(c.EquipmentId,c.DocumentId,c.ManualRevisionId,default));
    }
    [PostgresFact]public async Task EquipmentAuthorizationFiltersBeforePagination()
    {
        var allowed=await Create();await Create(); // Newer inaccessible equipment must not consume page one.
        var store=new PostgresConversationStore(database.Source);
        Assert.Equal(allowed.Id,Assert.Single(await store.ListAsync("owner",[allowed.EquipmentId],0,1,default)).Id);
        Assert.Empty(await store.ListAsync("owner",[],0,20,default));
    }
}
