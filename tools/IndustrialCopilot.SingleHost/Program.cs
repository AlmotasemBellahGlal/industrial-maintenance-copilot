using System.Security.Cryptography;
using IndustrialCopilot.Api;
using IndustrialCopilot.Application.Abstractions.AI;
using IndustrialCopilot.Application.Abstractions.Usage;
using IndustrialCopilot.Application.Knowledge;
using IndustrialCopilot.Corpus;
using IndustrialCopilot.Demo;
using IndustrialCopilot.Infrastructure.AI;
using IndustrialCopilot.Infrastructure.Operations;
using Microsoft.Extensions.DependencyInjection.Extensions;

// Demo only. Requires DEMO_SINGLE_HOST=true so it cannot start by accident, and the Production environment.
// All state is in PostgreSQL. Secrets are generated per start; only anonymous visitor sessions are used.
if(Environment.GetEnvironmentVariable("DEMO_SINGLE_HOST")!="true")throw new InvalidOperationException("Set DEMO_SINGLE_HOST=true. This single-process host is for the synthetic demo only.");
var equipment=Guid.Parse("11111111-1111-1111-1111-111111111111");
string Secret()=>Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
string Required(string key)=>Environment.GetEnvironmentVariable(key)??throw new InvalidOperationException(key+" is required.");
var connection=new Npgsql.NpgsqlConnectionStringBuilder(Required("ConnectionStrings__Operations")){GssEncryptionMode=Npgsql.GssEncryptionMode.Disable}.ConnectionString;
var contentRoot=AppContext.BaseDirectory;
var config=new Dictionary<string,string?>
{
    ["Safety:ProcedureFile"]=Path.Combine(contentRoot,"demo","reviewed-procedures.json"),
    ["ConnectionStrings:Operations"]=connection,["ConnectionStrings:Knowledge"]=connection,["ConnectionStrings:DispatchReceiver"]=connection,
    ["Dispatch:Adapter"]="PostgresInbox",["Worker:EquipmentIds"]=equipment.ToString(),
    // One static credential with look-and-start rights only, random secret, never shown. Public visitors use minted sessions with the same fixed rights.
    ["Authentication:Credentials:0:Actor"]="demo-technician",["Authentication:Credentials:0:Secret"]=Secret(),["Authentication:Credentials:0:Permissions"]="read,start",["Authentication:Credentials:0:EquipmentIds"]=equipment.ToString(),
    ["Llm:PrimaryProvider"]="Ollama",["Llm:EmbeddingProvider"]="Ollama",["Llm:FallbackEnabled"]="false",
    ["Llm:Ollama:Endpoint"]="http://127.0.0.1:11434/",["Llm:Ollama:ChatModel"]="demo-test-v1",["Llm:Ollama:EmbeddingModel"]="demo-test-v1",
    ["Llm:Ollama:RequestTimeoutSeconds"]="30",["Llm:Ollama:StreamTimeoutSeconds"]="60",
    ["Knowledge:EmbeddingProfile"]="synthetic-demo-v1",["Knowledge:EmbeddingRevision"]="deterministic-four-features-v1",["Knowledge:Dimensions"]="4",
    ["Knowledge:ChunkSize"]="4000",["Knowledge:Overlap"]="0",
    ["Logging:LogLevel:Microsoft.AspNetCore"]="Warning",
    ["Demo:PublicSessions"]="true",["Demo:EquipmentId"]=equipment.ToString(),
    ["Demo:MaxLiveSessions"]=Environment.GetEnvironmentVariable("DEMO_MAX_LIVE_SESSIONS")??"50",
    ["Demo:MaxSessionsPerClientPerHour"]=Environment.GetEnvironmentVariable("DEMO_MAX_SESSIONS_PER_CLIENT_HOUR")??"10",
};
if(Environment.GetEnvironmentVariable("DEMO_TRUSTED_PROXY_CIDR") is {Length:>0} cidr)config["Demo:TrustedProxyCidr"]=cidr;
await using var app=ApiHost.Build(args,b=>
{
    b.Configuration.AddInMemoryCollection(config);
    var identity=()=>HostAuthentication.Identity(new HttpContextAccessor().HttpContext);
    b.Services.AddMaintenanceHost(b.Configuration,identity,true);
    b.Services.Replace(ServiceDescriptor.Singleton<ILlmProvider>(p=>new AccountedLlmProvider(new DemoProvider(0),p.GetRequiredService<ILlmUsageStore>(),p.GetRequiredService<UsagePricing>(),"Demo","demo-test-v1","demo-test-v1",BillingKind.Synthetic)));
    // Only the reasoning worker and session cleanup run in this process. Dispatch reconciliation is NOT run here: it uses session-level advisory locks,
    // which are unsafe behind a transaction pooler, and public sessions cannot dispatch anyway.
    int Seconds(string name,int fallback)=>int.TryParse(Environment.GetEnvironmentVariable(name),out var v)&&v is>=1 and <=3600?v:fallback;
    b.Services.AddSingleton(new IndustrialCopilot.Worker.ReasoningSchedule(new HashSet<Guid>{equipment},concurrency:2,leaseSeconds:30,pollSeconds:Seconds("DEMO_POLL_SECONDS",1)));
    b.Services.AddHostedService<IndustrialCopilot.Worker.DemoSessionCleanup>();
    b.Services.AddHostedService<IndustrialCopilot.Worker.ReasoningWorker>();
});
// Operator-only surface is closed in this host: health, OpenAPI, usage, ingestion, dispatch. Visitors cannot reach these (no permission) and the paths are also refused here.
app.Use(async(c,next)=>
{
    var p=c.Request.Path.Value??"";
    bool Is(string x)=>p.StartsWith(x,StringComparison.OrdinalIgnoreCase);
    if(Is("/health")||Is("/openapi")||Is("/scalar")||Is("/api/usage")||Is("/api/documents")||Is("/api/dispatch-attempts")||(Is("/api/work-orders/")&&p.EndsWith("/dispatch",StringComparison.OrdinalIgnoreCase)))
    {c.Response.StatusCode=404;return;}
    await next();
});
// Same boundary as deploy/nginx.conf, since there is no nginx in this mode.
app.Use(async(c,next)=>
{
    c.Response.Headers["X-Content-Type-Options"]="nosniff";c.Response.Headers["X-Frame-Options"]="DENY";c.Response.Headers["Referrer-Policy"]="no-referrer";
    c.Response.Headers["Content-Security-Policy"]="default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; font-src 'self'; img-src 'self' data:; connect-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";
    await next();
});
app.UseDefaultFiles();app.UseStaticFiles();
app.MapFallback(async c=>
{
    var path=c.Request.Path.Value??"";
    if(path.StartsWith("/api/",StringComparison.OrdinalIgnoreCase)||path.StartsWith("/health/",StringComparison.OrdinalIgnoreCase)){c.Response.StatusCode=404;return;}
    var index=Path.Combine(app.Environment.WebRootPath??"","index.html");
    if(!File.Exists(index)){c.Response.StatusCode=404;return;}
    c.Response.ContentType="text/html";await c.Response.SendFileAsync(index);
});
if(args.Contains("--init"))
{
    // One-time data load, run from a trusted machine. Not run on startup.
    await MaintenanceHostRegistration.MigrateAsync(app.Services,true,default);
    await using(var s=File.OpenRead(Path.Combine(contentRoot,"demo","pump-manual.txt")))
        await app.Services.GetRequiredService<ManualIngestionService>().IngestAsync(new(Guid.Parse("22222222-2222-2222-2222-222222222222"),Guid.Parse("33333333-3333-3333-3333-333333333333"),"text/plain"),s,default);
    var ingest=app.Services.GetRequiredService<ManualIngestionService>();
    foreach(var document in AssessmentCorpus.Generate())
    {
        var r=document.Request;
        await app.Services.GetRequiredService<ProductCatalog>().RegisterAsync(equipment,r.DocumentId,r.ManualRevisionId,default);
        using var source=new MemoryStream(document.Bytes);await ingest.IngestAsync(r,source,default);
    }
    Console.WriteLine("Init complete.");return;
}
await MaintenanceHostRegistration.MigrateAsync(app.Services,true,default);
await app.RunAsync();
