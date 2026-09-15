using System.Text;
using IndustrialCopilot.Application.Abstractions.Documents;
using IndustrialCopilot.Application.Abstractions.Documents.Models;
using IndustrialCopilot.Application.Abstractions.Indexing.Models;
using IndustrialCopilot.Application.Abstractions.Retrieval.Models;
using IndustrialCopilot.Application.Knowledge;
using IndustrialCopilot.Infrastructure.Knowledge;
using IndustrialCopilot.Corpus;
namespace IndustrialCopilot.IntegrationTests.Knowledge;

public class IngestionPersistenceTests(KnowledgeDatabase database) : IClassFixture<KnowledgeDatabase>
{
    private readonly EmbeddingSpace space = new("corpus-test-v1", "synthetic-lexical-v1",32);
    private PostgresKnowledgeStore Store() => new(database.Source,new(space,"synthetic-lexical-v1"),new CorpusEmbeddings());
    private ManualIngestionService Service() => new(new DocumentPipeline(new ManualDocumentExtractor(),new DocumentCleaner(),new DeterministicDocumentChunker()),new CorpusEmbeddings(),Store(),space,reports:new PostgresIngestionReports(database.ConnectionString));
    private static async Task<int> Ingest(ManualIngestionService service,DocumentProcessingRequest request,byte[] bytes)
    { using var source=new MemoryStream(bytes); return await service.IngestAsync(request,source,default); }

    [PostgresFact]
    public async Task PdfPipelinePersistsMetadataAndAtomicSuccessAndConcurrentDuplicatesConverge()
    {
        var document=AssessmentCorpus.Generate()[0]; var request=new DocumentProcessingRequest(Guid.NewGuid(),Guid.NewGuid(),"application/pdf",document.Request.Metadata);
        var service=Service(); var first=await Ingest(service,request,document.Bytes);
        var before=await Store().RetrieveAsync(new("seal",100,request.DocumentId,request.ManualRevisionId),RetrievalMode.Dense,default);
        await Task.WhenAll(Enumerable.Range(0,4).Select(_=>Ingest(service,request,document.Bytes)));
        var after=await Store().RetrieveAsync(new("seal",100,request.DocumentId,request.ManualRevisionId),RetrievalMode.Dense,default);
        Assert.Equal(before.Select(c=>c.ChunkId),after.Select(c=>c.ChunkId)); Assert.Equal(first,after.Count);
        var reports=await new PostgresIngestionReports(database.ConnectionString).ReadAsync(request.DocumentId,request.ManualRevisionId,default);
        Assert.Equal(5,reports.Count); Assert.All(reports,r=>{Assert.Equal(IngestionState.Completed,r.State);Assert.Equal(IngestionStage.Indexing,r.Stage);Assert.Equal(5,r.Pages);Assert.Equal(first,r.Chunks);Assert.Equal(request.Metadata,r.Metadata);});
        await using var command=database.Source.CreateCommand("SELECT metadata->>'Source',page,locator FROM knowledge.chunks WHERE document_id=@doc");
        command.Parameters.AddWithValue("doc",request.DocumentId);await using var reader=await command.ExecuteReaderAsync();
        while(await reader.ReadAsync()){Assert.Equal(request.Metadata!.Source,reader.GetString(0));Assert.StartsWith($"pdf:page {reader.GetInt32(1)};",reader.GetString(2));}
    }
    [PostgresFact]
    public async Task FailedExtractionIsIsolatedAndPreservesPreviouslySearchableRevision()
    {
        var request=new DocumentProcessingRequest(Guid.NewGuid(),Guid.NewGuid(),"text/plain");var service=Service();
        await Ingest(service,request,"pump seal valid"u8.ToArray());
        var bad=new DocumentProcessingRequest(request.DocumentId,request.ManualRevisionId,"application/pdf");
        await Assert.ThrowsAsync<DocumentInputException>(()=>Ingest(service,bad,"confidential-parser-input"u8.ToArray()));
        var reports=await new PostgresIngestionReports(database.ConnectionString).ReadAsync(request.DocumentId,request.ManualRevisionId,default);
        Assert.Equal(IngestionFailure.InvalidDocument,reports[0].Failure);Assert.Equal(IngestionStage.Extracting,reports[0].Stage);
        Assert.Equal(IngestionState.Completed,reports[1].State);
        Assert.Single(await Store().RetrieveAsync(new("pump",10,request.DocumentId,request.ManualRevisionId),RetrievalMode.Keyword,default));
        var other=new DocumentProcessingRequest(Guid.NewGuid(),Guid.NewGuid(),"text/plain");
        await Ingest(service,other,"other pump"u8.ToArray());
        Assert.Equal(IngestionState.Completed,Assert.Single(await new PostgresIngestionReports(database.ConnectionString).ReadAsync(other.DocumentId,other.ManualRevisionId,default)).State);
    }
    [PostgresFact]
    public async Task SameRevisionChangesReplaceWhileNewRevisionPreservesOldAndEmptyDoesNotDelete()
    {
        var request=new DocumentProcessingRequest(Guid.NewGuid(),Guid.NewGuid(),"text/plain");var service=Service();
        await Ingest(service,request,"pump old"u8.ToArray());
        await Ingest(service,request,"pump replacement"u8.ToArray());
        var newer=new DocumentProcessingRequest(request.DocumentId,Guid.NewGuid(),"text/plain");
        await Ingest(service,newer,"pump revision two"u8.ToArray());
        Assert.Equal(0,await Ingest(service,request,"   "u8.ToArray()));
        Assert.Equal("pump replacement",Assert.Single(await Store().RetrieveAsync(new("pump",5,request.DocumentId,request.ManualRevisionId),RetrievalMode.Keyword,default)).Snippet);
        Assert.Equal("pump revision two",Assert.Single(await Store().RetrieveAsync(new("pump",5,request.DocumentId,newer.ManualRevisionId),RetrievalMode.Keyword,default)).Snippet);
        Assert.Equal(IngestionFailure.EmptyDocument,(await new PostgresIngestionReports(database.ConnectionString).ReadAsync(request.DocumentId,request.ManualRevisionId,default))[0].Failure);
    }
    [PostgresFact]
    public async Task InterruptedSessionIsDetectableAfterStoreRecreationAndCannotPublish()
    {
        var reports=new PostgresIngestionReports(database.ConnectionString);var request=new DocumentProcessingRequest(Guid.NewGuid(),Guid.NewGuid(),"text/plain");
        var attempt=await reports.BeginAsync(request,default);
        await attempt.AdvanceAsync(IngestionStage.Indexing,null,1,default);
        Assert.Equal(IngestionState.Processing,Assert.Single(await reports.ReadAsync(request.DocumentId,request.ManualRevisionId,default)).State);
        // Terminate the actual dedicated backend as a process-loss analogue, not just an in-memory fake.
        await using(var kill=database.Source.CreateCommand("SELECT pg_terminate_backend(backend_pid) FROM knowledge.ingestion_attempts WHERE id=@id"))
        {kill.Parameters.AddWithValue("id",attempt.Id);await kill.ExecuteNonQueryAsync();}
        await attempt.DisposeAsync();
        Assert.Equal(IngestionState.Interrupted,Assert.Single(await new PostgresIngestionReports(database.ConnectionString).ReadAsync(request.DocumentId,request.ManualRevisionId,default)).State);
        var chunk=new IndexedChunk(new(request.DocumentId,request.ManualRevisionId,Guid.NewGuid(),"line 1","pump"),Enumerable.Repeat(1f,32).ToArray());
        await Assert.ThrowsAsync<InvalidOperationException>(()=>Store().ReplaceRevisionAsync(new(request.DocumentId,request.ManualRevisionId,space.Profile,[chunk],attempt.Id),default));
        Assert.Empty(await Store().RetrieveAsync(new("pump",5,request.DocumentId,request.ManualRevisionId),RetrievalMode.Keyword,default));
    }
    [PostgresFact]
    public async Task CompletionAndRevisionCannotSplitAndLateFailureCannotOverwriteCommittedSuccess()
    {
        var reports=new PostgresIngestionReports(database.ConnectionString);var request=new DocumentProcessingRequest(Guid.NewGuid(),Guid.NewGuid(),"text/plain");
        await Ingest(Service(),request,"pump original"u8.ToArray());
        await using var attempt=await reports.BeginAsync(request,default);await attempt.AdvanceAsync(IngestionStage.Indexing,null,1,default);
        var chunk=new IndexedChunk(new(request.DocumentId,request.ManualRevisionId,Guid.NewGuid(),"line 1","pump replacement"),Enumerable.Repeat(1f,32).ToArray());
        // Wrong attempt identity fails AFTER the replacement inserts, proving the transaction rolls them back.
        await Assert.ThrowsAsync<InvalidOperationException>(()=>Store().ReplaceRevisionAsync(new(request.DocumentId,request.ManualRevisionId,space.Profile,[chunk],Guid.NewGuid()),default));
        Assert.Equal("pump original",Assert.Single(await Store().RetrieveAsync(new("pump",5,request.DocumentId,request.ManualRevisionId),RetrievalMode.Keyword,default)).Snippet);
        await Store().ReplaceRevisionAsync(new(request.DocumentId,request.ManualRevisionId,space.Profile,[chunk],attempt.Id),default);
        await attempt.FailAsync(IngestionFailure.StageFailed,default);
        Assert.Equal(IngestionState.Completed,(await reports.ReadAsync(request.DocumentId,request.ManualRevisionId,default))[0].State);
    }
}
