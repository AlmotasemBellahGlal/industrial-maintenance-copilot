using System.Text.Json;
using System.Text.Json.Serialization;
using IndustrialCopilot.Api;
using IndustrialCopilot.Infrastructure.Operations;
using Microsoft.AspNetCore.Authentication;
using Npgsql;

var ingestIndex=Array.IndexOf(args,"--ingest");
var statusIndex=Array.IndexOf(args,"--ingestion-status");
var metadataIndex=Array.IndexOf(args,"--ingest-metadata");
if(ingestIndex>=0 && args.Length<ingestIndex+4)throw new ArgumentException("--ingest requires document ID, revision ID and a local file path.");
if(statusIndex>=0 && args.Length<statusIndex+3)throw new ArgumentException("--ingestion-status requires document and revision IDs.");
if(metadataIndex>=0 && args.Length<metadataIndex+4)throw new ArgumentException("--ingest-metadata requires title, source label and revision number.");
var metadata=metadataIndex<0?null:new IndustrialCopilot.Application.Abstractions.Documents.Models.DocumentMetadata(args[metadataIndex+1],args[metadataIndex+2],int.Parse(args[metadataIndex+3]));
var hostArgs=args.ToList();
foreach(var option in new[]{(Index:ingestIndex,Count:4),(Index:statusIndex,Count:3),(Index:metadataIndex,Count:4)}.Where(o=>o.Index>=0).OrderByDescending(o=>o.Index))hostArgs.RemoveRange(option.Index,option.Count);
var app=ApiHost.Build(hostArgs.ToArray());
if(args.Contains("--migrate")) {await MaintenanceHostRegistration.MigrateAsync(app.Services,true,default);await app.DisposeAsync();return;}
if(statusIndex>=0)
{
    try
    {
        var reports=await app.Services.GetRequiredService<IndustrialCopilot.Application.Abstractions.Documents.IIngestionReports>()
            .ReadAsync(Guid.Parse(args[statusIndex+1]),Guid.Parse(args[statusIndex+2]),default);
        Console.WriteLine(JsonSerializer.Serialize(reports,new JsonSerializerOptions { WriteIndented=true, Converters={new JsonStringEnumConverter()} }));
    }
    catch(Exception) { Console.Error.WriteLine("Ingestion status unavailable. Verify identifiers and database availability."); Environment.ExitCode=1; }
    await app.DisposeAsync();return;
}
if(ingestIndex>=0)
{
    var document=Guid.Parse(args[ingestIndex+1]);var revision=Guid.Parse(args[ingestIndex+2]);
    if(!app.Services.GetRequiredService<IReadOnlyList<IndustrialCopilot.Application.Reasoning.ApprovedMaintenanceProcedure>>().Any(p=>p.Candidate.DocumentId==document && p.Candidate.ManualRevisionId==revision))throw new ArgumentException("Ingestion revision must be configured for this deployment.");
    using var shutdown=new CancellationTokenSource();Console.CancelKeyPress+=(_,e)=>{e.Cancel=true;shutdown.Cancel();};
    try
    {
        var path=args[ingestIndex+3];
        var media=Path.GetExtension(path).ToLowerInvariant() switch { ".txt"=>"text/plain", ".pdf"=>"application/pdf", _=>"application/unsupported" };
        await using var source=File.OpenRead(path);
        var count=await app.Services.GetRequiredService<IndustrialCopilot.Application.Knowledge.ManualIngestionService>().IngestAsync(
            new(document,revision,media,metadata),source,shutdown.Token);
        Console.WriteLine("Indexed chunks: "+count);
        if(count==0)Environment.ExitCode=1;
    }
    catch(Exception) { Console.Error.WriteLine("Ingestion failed. Inspect --ingestion-status for a safe stage/category; if no attempt exists, verify local input/configuration."); Environment.ExitCode=1; }
    await app.DisposeAsync();return;
}
await app.RunAsync();

public static class ApiHost
{
    public static WebApplication Build(string[] args,Action<WebApplicationBuilder>? configure=null)
    {
        var builder=WebApplication.CreateBuilder(args);
        if(Environment.GetEnvironmentVariable("MAINTENANCE_CONFIG") is {} configFile)builder.Configuration.AddJsonFile(configFile,false,false).AddEnvironmentVariables();
        configure?.Invoke(builder);
        builder.WebHost.ConfigureKestrel(o=>o.Limits.MaxRequestBodySize=128*1024);
        builder.Services.AddOpenApi();
        ApiSecurity.Register(builder);
        var askSeconds=builder.Configuration.GetValue<int?>("Ask:TimeoutSeconds")??60;
        if(askSeconds is <1 or >60)throw new InvalidOperationException("Ask timeout must be 1–60 seconds.");
        builder.Services.AddSingleton(new AskStreamLimits(askSeconds));
        builder.Services.AddLocalization(o=>o.ResourcesPath="Resources");
        builder.Services.Configure<RequestLocalizationOptions>(o=>
        {
            o.SetDefaultCulture("en-US").AddSupportedCultures("en-US","en","ar-EG","ar")
                .AddSupportedUICultures("en-US","en","ar-EG","ar");
            o.RequestCultureProviders=[new Microsoft.AspNetCore.Localization.AcceptLanguageHeaderRequestCultureProvider()];
            o.ApplyCurrentCultureToResponseHeaders=true;
        });
        builder.Services.ConfigureHttpJsonOptions(o=>o.SerializerOptions.UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow);
        builder.Services.AddSingleton(HostAuthentication.Read(builder.Configuration));
        builder.Services.AddAuthentication("HostCredential").AddScheme<AuthenticationSchemeOptions,HostAuthentication>("HostCredential",_=>{});
        builder.Services.AddAuthorization();
        var accessor=new HttpContextAccessor();builder.Services.AddSingleton<IHttpContextAccessor>(accessor);
        if(configure is null)builder.Services.AddMaintenanceHost(builder.Configuration,()=>HostAuthentication.Identity(accessor.HttpContext),true);
        int Number(string key,int fallback)=>builder.Configuration[key] is {} text?int.Parse(text):fallback;
        builder.Services.AddSingleton(new StreamingOptions(Number("Streaming:Capacity",32),Number("Streaming:WriteTimeoutSeconds",10)));
        var app=builder.Build();
        app.UseRequestLocalization();
        if (!app.Environment.IsDevelopment() && !app.Environment.IsEnvironment("Testing")) app.UseHsts();
        app.Use(async (context, next) =>
        {
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            context.Response.Headers["X-Frame-Options"] = "DENY";
            context.Response.Headers["Referrer-Policy"] = "no-referrer";
            context.Response.Headers["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'; base-uri 'none'";
            context.Response.Headers.CacheControl = "no-store";
            try { await next(context); }
            finally
            {
                // Route templates only: never log headers, request bodies, user paths or raw exceptions.
                if (context.Request.Path.StartsWithSegments("/api"))
                    app.Logger.LogInformation("Security HTTP {Method} {Route} status {Status} correlation {Correlation}",
                        context.Request.Method, (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? "unmatched",
                        context.Response.StatusCode, context.Items["correlation"]);
            }
        });
        app.UseCors();
        app.UseStatusCodePages(async status=>
        {
            var c=status.HttpContext;
            if(c.Request.Path.StartsWithSegments("/api"))
                await c.Response.WriteAsJsonAsync(ApiMessages.Failure(c,c.Response.StatusCode,
                    c.Response.StatusCode switch{401=>"unauthenticated",403=>"forbidden",404=>"not_found",_=>"invalid_request"}),c.RequestAborted);
        });
        app.Use(async(context,next)=>
        {
            try
            {
                var incoming=context.Request.Headers["X-Correlation-ID"].ToString();
                if(incoming.Length>0 && (!Guid.TryParse(incoming,out var valid)||valid==Guid.Empty))throw new ApiProblemException(400,"invalid_correlation");
                var correlation=incoming.Length==0?Guid.NewGuid():Guid.Parse(incoming);
                context.Items["correlation"]=correlation;HostAccess.Correlation.Value=correlation;context.Response.Headers["X-Correlation-ID"]=correlation.ToString("D");
                using var logScope=app.Logger.BeginScope(new Dictionary<string,object>{{"CorrelationId",correlation}});
                if(!context.Request.IsHttps && context.Request.Path.StartsWithSegments("/api") && !System.Net.IPAddress.IsLoopback(context.Connection.RemoteIpAddress??System.Net.IPAddress.None))throw new ApiProblemException(400,"https_required");
                if(!app.Environment.IsDevelopment() && !app.Environment.IsEnvironment("Testing") && !context.Request.IsHttps && context.Request.Path.StartsWithSegments("/api"))throw new ApiProblemException(400,"https_required");
                await next(context);
            }
            catch(OperationCanceledException) when(context.RequestAborted.IsCancellationRequested) {context.Abort();}
            catch(Exception error)
            {
                if(context.Response.HasStarted){context.Abort();return;}
                var (status,code)=error switch{BadHttpRequestException { StatusCode: 413 }=>(413,"payload_too_large"),ApiProblemException p=>(p.Status,p.Code),BadHttpRequestException or JsonException or ArgumentException=>(400,"invalid_request"),OperationCanceledException=>(504,"operation_timeout"),_=>(503,"dependency_unavailable")};
                context.Response.StatusCode=status;await context.Response.WriteAsJsonAsync(ApiMessages.Failure(context,status,code),context.RequestAborted);
            }
            finally{HostAccess.Correlation.Value=null;}
        });
        app.UseAuthentication();app.UseAuthorization();app.UseRateLimiter();
        app.MapGet("/health/live",()=>Results.Ok(new{status="alive"}));
        app.MapGet("/health/ready",async(HttpContext c)=>
        {
            try
            {
                using var timeout=CancellationTokenSource.CreateLinkedTokenSource(c.RequestAborted);timeout.CancelAfter(TimeSpan.FromSeconds(3));
                foreach(var key in new[]{"operations","receiver"})
                {
                    var source=c.RequestServices.GetRequiredKeyedService<NpgsqlDataSource>(key);
                    await using var command=source.CreateCommand(key=="operations"?"SELECT EXISTS(SELECT 1 FROM information_schema.columns WHERE table_schema='operations' AND table_name='dispatch_attempts' AND column_name='next_reconciliation_at') AND to_regclass('operations.reasoning_job_events') IS NOT NULL":"SELECT to_regclass('dispatch_receiver.tickets') IS NOT NULL");
                    if(await command.ExecuteScalarAsync(timeout.Token) is not true)return Results.StatusCode(503);
                }
                await using var knowledge=c.RequestServices.GetRequiredService<NpgsqlDataSource>().CreateCommand("SELECT to_regclass('knowledge.chunks') IS NOT NULL");
                if(await knowledge.ExecuteScalarAsync(timeout.Token) is not true)return Results.StatusCode(503);
                return Results.Ok(new{status="ready"});
            }
            catch{return Results.StatusCode(503);}
        });
        if(app.Environment.IsDevelopment())app.MapOpenApi();
        MaintenanceEndpoints.Map(app); ProductEndpoints.Map(app); ReasoningJobEndpoints.Map(app);
        return app;
    }
}
