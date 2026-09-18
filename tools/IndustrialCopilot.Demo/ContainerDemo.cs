using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using IndustrialCopilot.Api;
using IndustrialCopilot.Application.Abstractions.AI;
using IndustrialCopilot.Application.Abstractions.Documents;
using IndustrialCopilot.Application.Abstractions.Usage;
using IndustrialCopilot.Application.Knowledge;
using IndustrialCopilot.Corpus;
using IndustrialCopilot.Infrastructure.AI;
using IndustrialCopilot.Infrastructure.Operations;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace IndustrialCopilot.Demo;

/// <summary>Opt-in evaluator packaging for the existing synthetic harness; not registered by production API/Worker.</summary>
internal static class ContainerDemo
{
    private const string Secrets="/app-secrets", Config=Secrets+"/host.json";
    private static readonly Guid Equipment=Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static async Task RunAsync(string[] args)
    {
        if(Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER")!="true"
            || Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")!="Development")
            throw new InvalidOperationException("Container demo requires explicit Development container environment.");
        var operation=args.Last();
        if(operation=="setup") {Setup();return;}
        if(operation=="credentials")
        {
            // Explicit local operator command, never HTTP or automatic startup logs.
            Console.WriteLine("SYNTHETIC LOCAL DEMO ONLY\nSupervisor: "+File.ReadAllText(Secrets+"/supervisor")+"\nTechnician: "+File.ReadAllText(Secrets+"/technician"));return;
        }
        if(operation is "health-api" or "health-worker")
        {
            try
            {
                if(operation=="health-api")
                {
                    using var root=X509CertificateLoader.LoadCertificateFromFile("/public-ca/api.crt");
                    using var handler=new HttpClientHandler();
                    handler.ServerCertificateCustomValidationCallback=(_,cert,_,errors)=>
                    {
                        if(cert is null || (errors&SslPolicyErrors.RemoteCertificateNameMismatch)!=0)return false;
                        using var chain=new X509Chain();chain.ChainPolicy.TrustMode=X509ChainTrustMode.CustomRootTrust;
                        chain.ChainPolicy.CustomTrustStore.Add(root);chain.ChainPolicy.RevocationMode=X509RevocationMode.NoCheck;
                        return chain.Build(cert);
                    };
                    using var client=new HttpClient(handler){Timeout=TimeSpan.FromSeconds(4)};
                    (await client.GetAsync("https://localhost:8443/health/ready")).EnsureSuccessStatusCode();
                }
                else
                {
                    using var config=Load();await using var source=NpgsqlDataSource.Create(config["ConnectionStrings:Operations"]!);
                    using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(4));
                    await using var command=source.CreateCommand("SELECT to_regclass('operations.reasoning_jobs') IS NOT NULL AND to_regclass('operations.llm_usage') IS NOT NULL");
                    if(await command.ExecuteScalarAsync(timeout.Token) is not true)throw new InvalidOperationException();
                }
            }
            catch {Environment.ExitCode=1;}
            return;
        }
        using var settings=Load();
        if(operation=="api")
        {
            await using var app=ApiHost.Build(["--environment","Development"],b=>{
                b.Configuration.AddConfiguration(settings);b.WebHost.UseUrls("https://+:8443");
                Register(b.Services,b.Configuration,()=>HostAuthentication.Identity(new HttpContextAccessor().HttpContext));
            });
            await app.RunAsync();return;
        }
        var builder=Host.CreateApplicationBuilder();builder.Configuration.AddConfiguration(settings);
        Register(builder.Services,builder.Configuration,()=>new("reconciliation-worker",new HashSet<string>{"dispatch"},new HashSet<Guid>{Equipment}));
        if(operation=="worker")
        {
            builder.Services.AddSingleton(new IndustrialCopilot.Worker.ReconciliationSchedule(intervalSeconds:5));
            builder.Services.AddSingleton(new IndustrialCopilot.Worker.ReasoningSchedule(new HashSet<Guid>{Equipment},leaseSeconds:15));
            builder.Services.AddHostedService<IndustrialCopilot.Worker.Worker>();builder.Services.AddHostedService<IndustrialCopilot.Worker.ReasoningWorker>();
        }
        using var host=builder.Build();
        if(operation=="worker") {await host.RunAsync();return;}
        using var stop=new CancellationTokenSource();Console.CancelKeyPress+=(_,e)=>{e.Cancel=true;stop.Cancel();};
        if(operation=="migrate") {await MaintenanceHostRegistration.MigrateAsync(host.Services,true,stop.Token);Console.WriteLine("Migrations through 007 complete.");return;}
        var ingest=host.Services.GetRequiredService<ManualIngestionService>();
        if(operation=="seed")
        {
            await using var stream=File.OpenRead("/app/demo/pump-manual.txt");
            var count=await ingest.IngestAsync(new(Guid.Parse("22222222-2222-2222-2222-222222222222"),Guid.Parse("33333333-3333-3333-3333-333333333333"),"text/plain"),stream,stop.Token);
            if(count==0)throw new InvalidOperationException("Canonical seed produced no chunks.");
            Console.WriteLine($"Canonical synthetic manual ready: {count} chunks.");return;
        }
        if(operation is "corpus" or "corpus-status")
        {
            var corpus=AssessmentCorpus.Generate();var counts=AssessmentCorpus.Validate(corpus);
            foreach(var document in corpus)
            {
                var r=document.Request;
                if(operation=="corpus")
                {
                    if(!await host.Services.GetRequiredService<ProductCatalog>().RegisterAsync(Equipment,r.DocumentId,r.ManualRevisionId,stop.Token))throw new InvalidOperationException("Corpus identity conflict.");
                    using var source=new MemoryStream(document.Bytes);await ingest.IngestAsync(r,source,stop.Token);
                }
                var reports=await host.Services.GetRequiredService<IIngestionReports>().ReadAsync(r.DocumentId,r.ManualRevisionId,stop.Token);
                var latest=reports.FirstOrDefault();
                Console.WriteLine(JsonSerializer.Serialize(new{r.DocumentId,r.ManualRevisionId,State=latest?.State.ToString()}));
                if(latest?.State!=IngestionState.Completed)throw new InvalidOperationException("Corpus document is not completed.");
            }
            Console.WriteLine($"Completed assessment corpus: {counts.Documents} documents / {counts.Pages} PDF pages.");return;
        }
        throw new ArgumentException("Unknown container demo command.");
    }
    private static ConfigurationRoot Load()
    {
        var config=(ConfigurationRoot)new ConfigurationBuilder().AddJsonFile(Config,false).AddEnvironmentVariables().Build();
        // Retain the production host's safe framework logging level even though
        // its appsettings files are intentionally absent from the shared harness.
        config["Logging:LogLevel:Microsoft.AspNetCore"]="Warning";
        foreach(var name in new[]{"Operations","Knowledge","DispatchReceiver"})
        {
            // This private demo PostgreSQL uses generated password authentication,
            // not Kerberos. Do not probe unavailable GSS libraries on every connection.
            var connection=new NpgsqlConnectionStringBuilder(config["ConnectionStrings:"+name]){GssEncryptionMode=GssEncryptionMode.Disable};
            config["ConnectionStrings:"+name]=connection.ConnectionString;
        }
        var provider=Environment.GetEnvironmentVariable("DEMO_PROVIDER")??"Demo";
        if(provider is not ("Demo" or "OpenAi" or "Ollama"))throw new ArgumentException("DEMO_PROVIDER must be Demo, OpenAi or Ollama.");
        config["Llm:PrimaryProvider"]=config["Llm:EmbeddingProvider"]=provider=="Demo"?"Ollama":provider;
        if(provider=="Demo")
        {
            // Registration still validates normal provider options. This unused
            // loopback endpoint preserves the provider's HTTPS/non-loopback rule.
            config["Llm:Ollama:Endpoint"]="http://127.0.0.1:11434/";
            config["Llm:Ollama:ChatModel"]=config["Llm:Ollama:EmbeddingModel"]="demo-test-v1";
            config["Knowledge:EmbeddingProfile"]="synthetic-demo-v1";config["Knowledge:EmbeddingRevision"]="deterministic-four-features-v1";config["Knowledge:Dimensions"]="4";
        }
        return config;
    }
    private static void Register(IServiceCollection services,IConfiguration config,Func<HostIdentity?> identity)
    {
        services.AddMaintenanceHost(config,identity,true);
        var delay=int.Parse(Environment.GetEnvironmentVariable("DEMO_MODEL_DELAY_MS")??"0");
        if(delay is <0 or >30000)throw new ArgumentException("Invalid synthetic model delay.");
        if((Environment.GetEnvironmentVariable("DEMO_PROVIDER")??"Demo")=="Demo")
            services.Replace(ServiceDescriptor.Singleton<ILlmProvider>(p=>new AccountedLlmProvider(new DemoProvider(delay),p.GetRequiredService<ILlmUsageStore>(),p.GetRequiredService<UsagePricing>(),"Demo","demo-test-v1","demo-test-v1",BillingKind.Synthetic)));
    }
    private static void Setup()
    {
        Directory.CreateDirectory(Secrets);Directory.CreateDirectory("/db-secret");Directory.CreateDirectory("/public-ca");
        if(File.Exists(Config))
        {
            foreach(var path in new[]{Secrets+"/supervisor",Secrets+"/technician",Secrets+"/api.pfx","/db-secret/password","/public-ca/api.crt"})
                if(!File.Exists(path))throw new InvalidOperationException("Incomplete persisted setup. Restore matching secret volumes; do not reset database data.");
            Console.WriteLine("Existing demo secrets preserved.");return;
        }
        string Secret(string path)
        {
            if(!File.Exists(path))File.WriteAllText(path,Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));
            var value=File.ReadAllText(path);if(value.Length!=64 || !value.All(Uri.IsHexDigit))throw new InvalidOperationException("Invalid persisted demo secret.");return value;
        }
        var password=Secret("/db-secret/password");var supervisor=Secret(Secrets+"/supervisor");var technician=Secret(Secrets+"/technician");
        var certificatePassword=Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        using var key=RSA.Create(2048);var request=new CertificateRequest("CN=api",key,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1);
        var san=new SubjectAlternativeNameBuilder();san.AddDnsName("api");san.AddDnsName("localhost");request.CertificateExtensions.Add(san.Build());
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true,false,0,true));
        using var cert=request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5),DateTimeOffset.UtcNow.AddYears(1));
        File.WriteAllBytes(Secrets+"/api.pfx",cert.Export(X509ContentType.Pfx,certificatePassword));File.WriteAllText("/public-ca/api.crt",cert.ExportCertificatePem());
        var connection=new NpgsqlConnectionStringBuilder{Host="db",Database="maintenance_demo",Username="postgres",Password=password}.ConnectionString;
        var config=new Dictionary<string,string?>{
            ["Safety:ProcedureFile"]="/app/demo/reviewed-procedures.json",["ConnectionStrings:Operations"]=connection,["ConnectionStrings:Knowledge"]=connection,["ConnectionStrings:DispatchReceiver"]=connection,
            ["Dispatch:Adapter"]="PostgresInbox",["Worker:EquipmentIds"]=Equipment.ToString(),
            ["Authentication:Credentials:0:Actor"]="demo-supervisor",["Authentication:Credentials:0:Secret"]=supervisor,["Authentication:Credentials:0:Permissions"]="read,start,approve,verify,dispatch,ingest",["Authentication:Credentials:0:EquipmentIds"]=Equipment.ToString(),
            ["Authentication:Credentials:1:Actor"]="demo-technician",["Authentication:Credentials:1:Secret"]=technician,["Authentication:Credentials:1:Permissions"]="read,start",["Authentication:Credentials:1:EquipmentIds"]=Equipment.ToString(),
            ["Kestrel:Certificates:Default:Path"]=Secrets+"/api.pfx",["Kestrel:Certificates:Default:Password"]=certificatePassword,
            ["Llm:FallbackEnabled"]="false",["Llm:Ollama:Endpoint"]="https://host.docker.internal:11434/",["Llm:Ollama:ChatModel"]="demo-test-v1",["Llm:Ollama:EmbeddingModel"]="demo-test-v1",
            ["Llm:Ollama:RequestTimeoutSeconds"]="30",["Llm:Ollama:StreamTimeoutSeconds"]="60",
            ["Llm:OpenAi:Endpoint"]="https://api.openai.com/v1/",["Llm:OpenAi:RequestTimeoutSeconds"]="30",["Llm:OpenAi:StreamTimeoutSeconds"]="60",
            ["Knowledge:ChunkSize"]="4000",["Knowledge:Overlap"]="0"
        };
        File.WriteAllText(Config,JsonSerializer.Serialize(config));Console.WriteLine("Random demo credentials and internal TLS ready. Retrieve credentials with the explicit credentials command.");
    }
}
