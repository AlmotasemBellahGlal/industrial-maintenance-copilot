using System.Security.Cryptography;
using System.Text.Json;
using IndustrialCopilot.Api;
using IndustrialCopilot.Application.Abstractions.AI;
using IndustrialCopilot.Application.Knowledge;
using IndustrialCopilot.Demo;
using IndustrialCopilot.Infrastructure.Operations;
using Microsoft.Extensions.DependencyInjection.Extensions;

// Separate opt-in harness: cannot be activated through production API configuration.
if(!args.Contains("--demo") || Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")!="Development")
    throw new InvalidOperationException("Use --demo with ASPNETCORE_ENVIRONMENT=Development. Never deploy this executable.");
var root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../"));
var connection=Environment.GetEnvironmentVariable("DEMO_POSTGRES")??throw new InvalidOperationException("DEMO_POSTGRES is required.");
var parsed=new Npgsql.NpgsqlConnectionStringBuilder(connection);
if(parsed.Host is not ("127.0.0.1" or "localhost") || parsed.Database!="maintenance_demo")
    throw new InvalidOperationException("Demo requires a loopback maintenance_demo database.");
if(args.Contains("--worker"))
{
    // Same production hosted services; only this opt-in executable substitutes the deterministic model.
    var builder=Host.CreateApplicationBuilder();
    builder.Configuration.AddJsonFile(Path.Combine(root,"artifacts/issue25/host.json"),false,false);
    var ids=(builder.Configuration["Worker:EquipmentIds"]??"").Split(',').Select(Guid.Parse).ToHashSet();
    var identity=new HostIdentity("reconciliation-worker",new HashSet<string>{"dispatch"},ids);
    builder.Services.AddMaintenanceHost(builder.Configuration,()=>identity,true);
    var delay=int.TryParse(Environment.GetEnvironmentVariable("DEMO_MODEL_DELAY_MS"),out var configuredDelay)?configuredDelay:0;
    if(delay is <0 or >30000)throw new ArgumentException("Invalid test-only model delay.");
    builder.Services.Replace(ServiceDescriptor.Singleton<ILlmProvider>(new DemoProvider(delay)));
    builder.Services.AddSingleton(new IndustrialCopilot.Worker.ReconciliationSchedule(intervalSeconds:5));
    builder.Services.AddSingleton(new IndustrialCopilot.Worker.ReasoningSchedule(ids,leaseSeconds:8));
    builder.Services.AddHostedService<IndustrialCopilot.Worker.Worker>();
    builder.Services.AddHostedService<IndustrialCopilot.Worker.ReasoningWorker>();
    using var host=builder.Build();await host.RunAsync();return;
}
var token=Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
var technicianToken=Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
var equipment="11111111-1111-1111-1111-111111111111";
var config=new Dictionary<string,string?>
{
    ["Safety:ProcedureFile"]=Path.Combine(root,"demo/reviewed-procedures.json"),
    ["ConnectionStrings:Operations"]=connection,["ConnectionStrings:Knowledge"]=connection,["ConnectionStrings:DispatchReceiver"]=connection,
    ["Dispatch:Adapter"]="PostgresInbox",["Worker:EquipmentIds"]=equipment,["Worker:IntervalSeconds"]="5",
    ["Authentication:Credentials:0:Actor"]="demo-supervisor",["Authentication:Credentials:0:Secret"]=token,
    ["Authentication:Credentials:0:Permissions"]="read,start,approve,verify,dispatch",["Authentication:Credentials:0:EquipmentIds"]=equipment,
    ["Authentication:Credentials:1:Actor"]="demo-technician",["Authentication:Credentials:1:Secret"]=technicianToken,
    ["Authentication:Credentials:1:Permissions"]="read,start",["Authentication:Credentials:1:EquipmentIds"]=equipment,
    ["Security:AllowedOrigins:0"]="http://127.0.0.1:4300",
    ["Llm:PrimaryProvider"]="Ollama",["Llm:EmbeddingProvider"]="Ollama",["Llm:FallbackEnabled"]="false",
    ["Llm:Ollama:Endpoint"]="http://127.0.0.1:11434/",["Llm:Ollama:ChatModel"]="demo-test-v1",["Llm:Ollama:EmbeddingModel"]="demo-test-v1",
    ["Llm:Ollama:RequestTimeoutSeconds"]="30",["Llm:Ollama:StreamTimeoutSeconds"]="60",
    ["Knowledge:EmbeddingProfile"]="synthetic-demo-v1",["Knowledge:EmbeddingRevision"]="deterministic-four-features-v1",
    ["Knowledge:Dimensions"]="4",["Knowledge:ChunkSize"]="4000",["Knowledge:Overlap"]="0"
};
await using var app=ApiHost.Build(["--environment","Development"],b=>
{
    b.WebHost.UseUrls("http://127.0.0.1:5000");
    b.Configuration.AddInMemoryCollection(config);
    b.Services.AddMaintenanceHost(b.Configuration,()=>HostAuthentication.Identity(new HttpContextAccessor().HttpContext),true);
    b.Services.Replace(ServiceDescriptor.Singleton<ILlmProvider,DemoProvider>());
    if(args.Contains("--uncertain"))
        b.Services.Replace(ServiceDescriptor.Singleton<IndustrialCopilot.Application.Abstractions.Actions.IExternalDispatch>(p=>
            new UncertainDemoReceiver(new(p.GetRequiredKeyedService<Npgsql.NpgsqlDataSource>("receiver")))));
});
await MaintenanceHostRegistration.MigrateAsync(app.Services,true,default);
await using(var source=File.OpenRead(Path.Combine(root,"demo/pump-manual.txt")))
    Console.WriteLine("Demo chunks indexed: "+await app.Services.GetRequiredService<ManualIngestionService>().IngestAsync(
        new(Guid.Parse("22222222-2222-2222-2222-222222222222"),Guid.Parse("33333333-3333-3333-3333-333333333333"),"text/plain"),source,default));
var artifacts=Path.Combine(root,"artifacts/issue25");Directory.CreateDirectory(artifacts);
// Ephemeral credentials/config are ignored artifacts; never printed in logs.
await File.WriteAllTextAsync(Path.Combine(artifacts,"host.json"),JsonSerializer.Serialize(config));
await File.WriteAllTextAsync(Path.Combine(artifacts,"credential.txt"),token);
await File.WriteAllTextAsync(Path.Combine(artifacts,"technician-credential.txt"),technicianToken);
Console.WriteLine("SYNTHETIC DEMO ONLY. Credential and Worker configuration: artifacts/issue25 (untracked).");
await app.RunAsync();
