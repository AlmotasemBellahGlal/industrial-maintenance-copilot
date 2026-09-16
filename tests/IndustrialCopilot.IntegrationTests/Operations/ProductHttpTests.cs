using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using IndustrialCopilot.Api;
using IndustrialCopilot.Application.Reasoning;
using IndustrialCopilot.Application.Abstractions.Usage;
using IndustrialCopilot.Infrastructure.AI;
using IndustrialCopilot.Application.Ask;
using IndustrialCopilot.Application.Abstractions.AI;
using IndustrialCopilot.Application.Abstractions.AI.Models;
using IndustrialCopilot.Application.Abstractions.Retrieval;
using IndustrialCopilot.Application.Abstractions.Retrieval.Models;
using IndustrialCopilot.Infrastructure.Operations;
using IndustrialCopilot.IntegrationTests.Knowledge;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
namespace IndustrialCopilot.IntegrationTests.Operations;
public class ProductHttpTests(KnowledgeDatabase database):IClassFixture<KnowledgeDatabase>
{
    private sealed class Provider:ILlmProvider
    {
        public bool Wait,Fail;public bool FailTools;public int ToolCalls;public readonly TaskCompletionSource Stopped=new(TaskCreationOptions.RunContinuationsAsynchronously);public bool Cancelled;
        public async IAsyncEnumerable<StreamingChunk> StreamAsync(CompletionRequest r,[EnumeratorCancellation]CancellationToken ct)
        {
            try{yield return new("first ",false);await Task.Delay(Wait?Timeout.Infinite:20,ct);if(Fail)throw new InvalidOperationException("PRIVATE PROVIDER DATA");yield return new("second",true);}
            finally{Cancelled=ct.IsCancellationRequested;Stopped.TrySetResult();}
        }
        public Task<EmbeddingResult> GenerateEmbeddingsAsync(EmbeddingRequest r,CancellationToken ct)=>Task.FromResult(new EmbeddingResult(r.Inputs.Select(_=>(IReadOnlyList<float>)new float[]{1,0}).ToArray(),"test-model"));
        public Task<CompletionResponse> CompleteAsync(CompletionRequest r,CancellationToken ct)=>throw new NotSupportedException();
        public Task<ToolCompletionResponse> CompleteWithToolsAsync(CompletionRequest r,IReadOnlyList<ToolDefinition> t,CancellationToken ct){ToolCalls++;throw FailTools?new DependencyFailureException(DependencyFailureKind.Transient):new NotSupportedException();}
    }
    private async Task Run(Func<HttpClient,IServiceProvider,Provider,Guid,Task> test,int timeout=60,int permits=30)
    {
        var equipment=Guid.NewGuid();var file=Path.GetTempFileName();var provider=new Provider();
        try{
            await File.WriteAllTextAsync(file,JsonSerializer.Serialize(new[]{new{Candidate=new{EquipmentId=equipment,EquipmentName="pump",DocumentId=Guid.NewGuid(),ManualRevisionId=Guid.NewGuid()},DiagnosticInstructions=new[]{"inspect"},WorkInstructions=new[]{"repair"},WorkOrderDescription="reviewed",Requirements=Array.Empty<object>(),ExplicitlyNoMandatoryRequirements=true}}));
            await using var app=ApiHost.Build(["--environment","Testing"],b=>{
                b.WebHost.UseUrls("http://127.0.0.1:0");b.Logging.ClearProviders();
                b.Configuration.AddInMemoryCollection(new Dictionary<string,string?>{
                    ["Security:MutationPermits"]=permits.ToString(),["Ask:TimeoutSeconds"]=timeout.ToString(),["Safety:ProcedureFile"]=file,["ConnectionStrings:Operations"]=database.ConnectionString,["ConnectionStrings:Knowledge"]=database.ConnectionString,["ConnectionStrings:DispatchReceiver"]=database.ConnectionString,["Dispatch:Adapter"]="PostgresInbox",
                    ["Authentication:Credentials:0:Actor"]="owner",["Authentication:Credentials:0:Secret"]=new('a',32),["Authentication:Credentials:0:Permissions"]="read,start,ingest,approve",["Authentication:Credentials:0:EquipmentIds"]=equipment.ToString(),
                    ["Authentication:Credentials:1:Actor"]="other",["Authentication:Credentials:1:Secret"]=new('b',32),["Authentication:Credentials:1:Permissions"]="read,start",["Authentication:Credentials:1:EquipmentIds"]=equipment.ToString(),
                    ["Llm:Ollama:RequestTimeoutSeconds"]="30",["Llm:Ollama:StreamTimeoutSeconds"]="60",["Llm:PrimaryProvider"]="Ollama",["Llm:EmbeddingProvider"]="Ollama",["Llm:FallbackEnabled"]="false",["Llm:Ollama:Endpoint"]="http://127.0.0.1:11434/",["Llm:Ollama:ChatModel"]="test-model",["Llm:Ollama:EmbeddingModel"]="test-model",
                    ["Knowledge:EmbeddingProfile"]="test-v1",["Knowledge:EmbeddingRevision"]="test",["Knowledge:Dimensions"]="2"});
                b.Services.AddMaintenanceHost(b.Configuration,()=>HostAuthentication.Identity(new HttpContextAccessor().HttpContext),true);
                b.Services.Replace(ServiceDescriptor.Singleton<ILlmProvider>(s=>new AccountedLlmProvider(provider,s.GetRequiredService<ILlmUsageStore>(),new(),"Test","test-model","test-model",BillingKind.Synthetic)));
            });
            await MaintenanceHostRegistration.MigrateAsync(app.Services,true,default);await app.StartAsync();
            using var client=new HttpClient{BaseAddress=new(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()),Timeout=TimeSpan.FromSeconds(20)};
            client.DefaultRequestHeaders.Authorization=new("Bearer",new string('a',32));
            await test(client,app.Services,provider,equipment);await app.StopAsync();
        }finally{File.Delete(file);}
    }
    private static async Task<Guid> IngestAndCreate(HttpClient client,Guid equipment)
    {
        var document=Guid.NewGuid();var revision=Guid.NewGuid();
        var url=$"/api/documents/{document}/revisions/{revision}/ingest?equipmentId={equipment}&filename=pump.txt&title=Pump&revisionNumber=1";
        for(var n=0;n<2;n++){using var content=new StringContent("Pump vibration requires inspection. اهتزاز المضخة يتطلب الفحص.");content.Headers.ContentType=new("text/plain");var ingest=await client.PostAsync(url,content);Assert.Equal(HttpStatusCode.OK,ingest.StatusCode);var reports=await ingest.Content.ReadFromJsonAsync<JsonElement>();Assert.Equal(2,reports[0].GetProperty("state").GetInt32());}
        var status=await client.GetAsync($"/api/documents/{document}/revisions/{revision}/ingestion?equipmentId={equipment}");Assert.Equal(HttpStatusCode.OK,status.StatusCode);
        var create=await client.PostAsJsonAsync("/api/conversations",new{equipmentId=equipment,documentId=document,manualRevisionId=revision});Assert.Equal(HttpStatusCode.OK,create.StatusCode);
        return (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }
    [PostgresFact]public async Task RealHttpTransientExhaustionProducesGroundedAdvisoryFallbackAndCorrelatedUsage()=>await Run(async(client,services,provider,equipment)=>{
        var candidate=services.GetRequiredService<IReadOnlyList<ApprovedMaintenanceProcedure>>().Single().Candidate;
        using var content=new StringContent("Pump vibration requires inspection. Exact manual evidence.");content.Headers.ContentType=new("text/plain");
        var ingest=await client.PostAsync($"/api/documents/{candidate.DocumentId}/revisions/{candidate.ManualRevisionId}/ingest?equipmentId={equipment}&filename=pump.txt&title=Pump&revisionNumber=1",content);Assert.Equal(HttpStatusCode.OK,ingest.StatusCode);
        provider.FailTools=true;var correlation=Guid.NewGuid();client.DefaultRequestHeaders.Add("X-Correlation-ID",correlation.ToString());
        var response=await client.PostAsJsonAsync("/api/runs",new{equipmentId=equipment,symptom="pump vibration"});Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        var result=await response.Content.ReadFromJsonAsync<JsonElement>();Assert.Equal("Degraded",result.GetProperty("outcome").GetString());Assert.Equal(JsonValueKind.Null,result.GetProperty("workOrderId").ValueKind);Assert.Equal("transient_exhausted",result.GetProperty("degradationReason").GetString());
        var citation=Assert.Single(result.GetProperty("citations").EnumerateArray());Assert.Equal(candidate.DocumentId,citation.GetProperty("documentId").GetGuid());Assert.Equal(candidate.ManualRevisionId,citation.GetProperty("manualRevisionId").GetGuid());Assert.Contains("Exact manual evidence.",citation.GetProperty("snippet").GetString());
        Assert.Equal(2,provider.ToolCalls);
        var usage=(await client.GetFromJsonAsync<UsagePage>($"/api/usage?correlationId={correlation}"))!;
        Assert.Equal(2,usage.Records.Count(r=>r.Operation==LlmOperation.ToolCompletion));Assert.Contains(usage.Records,r=>r.Context.Purpose==UsagePurpose.GroundedFallback && r.Operation==LlmOperation.Streaming);
        Assert.All(usage.Records,r=>{Assert.Equal(result.GetProperty("runId").GetGuid(),r.Context.RunId);Assert.NotNull(r.Context.StepId);Assert.Null(r.EstimatedCost);});
        var trace=await client.GetFromJsonAsync<JsonElement>($"/api/traces/{result.GetProperty("executionId").GetGuid()}");
        var stepIds=trace.GetProperty("steps").EnumerateArray().Select(x=>x.GetProperty("stepId").GetGuid()).ToHashSet();
        Assert.All(usage.Records,r=>Assert.Contains(r.Context.StepId!.Value,stepIds));
    });
    [PostgresFact]public async Task UsageApiPersistsScopedCallsAndRejectsAnonymousAccess()=>await Run(async(client,services,provider,equipment)=>{
        var id=await IngestAndCreate(client,equipment);var correlation=Guid.NewGuid();client.DefaultRequestHeaders.Add("X-Correlation-ID",correlation.ToString());
        await client.PostAsJsonAsync($"/api/conversations/{id}/ask",new{question="pump"});
        var page=await client.GetFromJsonAsync<UsagePage>($"/api/usage?correlationId={correlation}");
        Assert.NotNull(page);Assert.NotEmpty(page.Records);Assert.Contains(page.Records,r=>r.Operation==LlmOperation.Streaming && r.Status==UsageStatus.Succeeded);
        Assert.All(page.Records,r=>{Assert.Equal("owner",r.Context.Actor);Assert.Equal(equipment,r.Context.EquipmentId);Assert.Null(r.Tokens);Assert.Null(r.EstimatedCost);});
        Assert.Equal(HttpStatusCode.BadRequest,(await client.GetAsync("/api/usage?limit=101")).StatusCode);
        client.DefaultRequestHeaders.Authorization=new("Bearer",new string('b',32));Assert.Empty((await client.GetFromJsonAsync<UsagePage>($"/api/usage?correlationId={correlation}"))!.Records);
        client.DefaultRequestHeaders.Authorization=null;Assert.Equal(HttpStatusCode.Unauthorized,(await client.GetAsync("/api/usage")).StatusCode);
    });
    [PostgresFact]public async Task RealHttpIngestionAskHistoryOwnerIsolationAndSafeErrors()=>await Run(async(client,services,provider,equipment)=>{
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("ar-EG");var id=await IngestAndCreate(client,equipment);
        var response=await client.PostAsJsonAsync($"/api/conversations/{id}/ask",new{question="pump"});var text=await response.Content.ReadAsStringAsync();
        Assert.Contains("event: delta",text);Assert.Contains("first ",text);Assert.Contains("second",text);Assert.Contains("event: citation",text);Assert.Contains("event: completed",text);Assert.DoesNotContain("event: reasoning",text);
        var history=await client.GetFromJsonAsync<JsonElement>($"/api/conversations/{id}");Assert.Equal("ar-EG",history.GetProperty("conversation").GetProperty("culture").GetString());Assert.Equal("first second",history.GetProperty("turns")[0].GetProperty("answer").GetString());
        Assert.NotEmpty(history.GetProperty("turns")[0].GetProperty("citations").EnumerateArray());
        var refusal=await client.PostAsJsonAsync($"/api/conversations/{id}/ask",new{question="quasar astrophysics"});Assert.Contains("InsufficientEvidence",await refusal.Content.ReadAsStringAsync());
        provider.Fail=true;var failed=await client.PostAsJsonAsync($"/api/conversations/{id}/ask",new{question="pump"});var safe=await failed.Content.ReadAsStringAsync();Assert.Contains("event: error",safe);Assert.DoesNotContain("PRIVATE",safe);
        Assert.Equal(HttpStatusCode.BadRequest,(await client.PostAsJsonAsync($"/api/conversations/{id}/ask",new{question=new string('x',2001)})).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await client.GetAsync($"/api/conversations/{id}?limit=51")).StatusCode);
        client.DefaultRequestHeaders.Authorization=new("Bearer",new string('b',32));
        Assert.Equal(HttpStatusCode.NotFound,(await client.GetAsync($"/api/conversations/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await client.PostAsJsonAsync($"/api/conversations/{id}/ask",new{question="pump"})).StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<JsonElement>("/api/conversations")).EnumerateArray());
        Assert.Equal(HttpStatusCode.Forbidden,(await client.PostAsync($"/api/documents/{Guid.NewGuid()}/revisions/{Guid.NewGuid()}/ingest?equipmentId={equipment}&filename=pump.txt&title=Pump&revisionNumber=1",new StringContent("pump"))).StatusCode);
        client.DefaultRequestHeaders.Authorization=null;Assert.Equal(HttpStatusCode.Unauthorized,(await client.PostAsJsonAsync($"/api/conversations/{id}/ask",new{question="pump"})).StatusCode);
    });
    [PostgresFact]public async Task HttpDisconnectCancelsProviderAndPersistsCancelledTurn()=>await Run(async(client,services,provider,equipment)=>{
        var id=await IngestAndCreate(client,equipment);provider.Wait=true;
        using(var request=new HttpRequestMessage(HttpMethod.Post,$"/api/conversations/{id}/ask"){Content=JsonContent.Create(new{question="pump"})})
        using(var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead))
        {
            using var reader=new StreamReader(await response.Content.ReadAsStreamAsync());
            while(await reader.ReadLineAsync() is {} line)if(line.Contains("first "))break;
            Assert.False(provider.Stopped.Task.IsCompleted);
            Assert.Equal(HttpStatusCode.Conflict,(await client.PostAsJsonAsync($"/api/conversations/{id}/ask",new{question="pump"})).StatusCode);
        }
        await provider.Stopped.Task.WaitAsync(TimeSpan.FromSeconds(8));Assert.True(provider.Cancelled);
        var store=services.GetRequiredService<IConversationStore>();IReadOnlyList<AskTurn> turns=[];
        for(var n=0;n<40;n++){turns=await store.TurnsAsync("owner",id,0,20,default);if(turns[0].State!=AskState.Streaming)break;await Task.Delay(50);}
        Assert.Equal(AskState.Cancelled,Assert.Single(turns).State);Assert.Empty(turns[0].Answer);
    });
    [PostgresFact]public async Task UploadRejectsPathsUnsupportedMediaAndSizeBeforeProcessing()=>await Run(async(client,services,provider,equipment)=>{
        var baseUrl=$"/api/documents/{Guid.NewGuid()}/revisions/{Guid.NewGuid()}/ingest?equipmentId={equipment}&title=Pump&revisionNumber=1&filename=";
        using var path=new StringContent("pump");Assert.Equal(HttpStatusCode.BadRequest,(await client.PostAsync(baseUrl+"..%2Fescape.txt",path)).StatusCode);
        using var unsupported=new ByteArrayContent([1]);unsupported.Headers.ContentType=new("application/octet-stream");Assert.Equal(HttpStatusCode.BadRequest,(await client.PostAsync(baseUrl+"a.exe",unsupported)).StatusCode);
        using var large=new ByteArrayContent(new byte[16000001]);large.Headers.ContentType=new("text/plain");
        using var oversized=new HttpRequestMessage(HttpMethod.Post,baseUrl+"a.txt"){Content=large};oversized.Headers.ExpectContinue=true;
        using var rejected=await client.SendAsync(oversized,HttpCompletionOption.ResponseHeadersRead);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge,rejected.StatusCode);
        using var corrupt=new StringContent("not a pdf");corrupt.Headers.ContentType=new("application/pdf");Assert.Equal(HttpStatusCode.UnprocessableEntity,(await client.PostAsync(baseUrl+"a.pdf",corrupt)).StatusCode);
    });
    [PostgresFact]public async Task DeadlineStopsProviderAndPersistsSafeFailure()=>await Run(async(client,services,provider,equipment)=>{
        var id=await IngestAndCreate(client,equipment);provider.Wait=true;
        var response=await client.PostAsJsonAsync($"/api/conversations/{id}/ask",new{question="pump"});
        var body=await response.Content.ReadAsStringAsync();Assert.Contains("operation_timeout",body);Assert.True(provider.Cancelled);
        Assert.Equal(AskState.Failed,Assert.Single(await services.GetRequiredService<IConversationStore>().TurnsAsync("owner",id,0,20,default)).State);
    },timeout:1);
    [PostgresFact]public async Task PdfHttpUsesProductionExtractionWithPageProvenance()=>await Run(async(client,services,provider,equipment)=>{
        var source=IndustrialCopilot.Corpus.AssessmentCorpus.Generate()[0];
        var document=Guid.NewGuid();var revision=Guid.NewGuid();
        using var content=new ByteArrayContent(source.Bytes);content.Headers.ContentType=new("application/pdf");
        var response=await client.PostAsync($"/api/documents/{document}/revisions/{revision}/ingest?equipmentId={equipment}&filename=manual.pdf&title=Pump&revisionNumber=1",content);
        Assert.Equal(HttpStatusCode.OK,response.StatusCode);var report=(await response.Content.ReadFromJsonAsync<JsonElement>())[0];Assert.Equal(5,report.GetProperty("pages").GetInt32());Assert.True(report.GetProperty("chunks").GetInt32()>0);
        var results=await services.GetRequiredService<IndustrialCopilot.Infrastructure.Knowledge.PostgresKnowledgeStore>().RetrieveAsync(new("seal",5,document,revision),RetrievalMode.Keyword,default);
        Assert.NotEmpty(results);Assert.All(results,r=>Assert.StartsWith("pdf:page",r.Locator));
    });
    [PostgresFact]public async Task AskSharesActorMutationBudgetAndDoesNotRetryRejectedGeneration()=>await Run(async(client,services,provider,equipment)=>{
        var id=await IngestAndCreate(client,equipment); // Three POSTs, including the two idempotent uploads.
        Assert.Equal(HttpStatusCode.OK,(await client.PostAsJsonAsync($"/api/conversations/{id}/ask",new{question="pump"})).StatusCode);
        var rejected=await client.PostAsJsonAsync($"/api/conversations/{id}/ask",new{question="pump"});
        Assert.Equal(HttpStatusCode.TooManyRequests,rejected.StatusCode);Assert.NotNull(rejected.Headers.RetryAfter);
        Assert.Single(await services.GetRequiredService<IConversationStore>().TurnsAsync("owner",id,0,20,default));
    },permits:4);
}
