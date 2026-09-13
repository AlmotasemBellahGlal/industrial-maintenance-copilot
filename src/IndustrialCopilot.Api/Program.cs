using System.Text.Json;
using System.Text.Json.Serialization;
using IndustrialCopilot.Api;
using IndustrialCopilot.Infrastructure.Operations;
using Microsoft.AspNetCore.Authentication;
using Npgsql;

var ingestIndex=Array.IndexOf(args,"--ingest");
if(ingestIndex>=0 && args.Length<ingestIndex+4)throw new ArgumentException("--ingest requires document ID, revision ID and a text file path.");
var app=ApiHost.Build(ingestIndex<0?args:args.Take(ingestIndex).Concat(args.Skip(ingestIndex+4)).ToArray());
if(args.Contains("--migrate")) {await MaintenanceHostRegistration.MigrateAsync(app.Services,true,default);await app.DisposeAsync();return;}
if(ingestIndex>=0)
{
    var document=Guid.Parse(args[ingestIndex+1]);var revision=Guid.Parse(args[ingestIndex+2]);
    if(!app.Services.GetRequiredService<IReadOnlyList<IndustrialCopilot.Application.Reasoning.ApprovedMaintenanceProcedure>>().Any(p=>p.Candidate.DocumentId==document && p.Candidate.ManualRevisionId==revision))throw new ArgumentException("Ingestion revision must be configured for this deployment.");
    using var shutdown=new CancellationTokenSource();Console.CancelKeyPress+=(_,e)=>{e.Cancel=true;shutdown.Cancel();};
    await using var source=File.OpenRead(args[ingestIndex+3]);
    var count=await app.Services.GetRequiredService<IndustrialCopilot.Application.Knowledge.ManualIngestionService>().IngestAsync(new(document,revision,"text/plain"),source,shutdown.Token);
    Console.WriteLine("Indexed chunks: "+count);await app.DisposeAsync();return;
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
        builder.Services.ConfigureHttpJsonOptions(o=>o.SerializerOptions.UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow);
        builder.Services.AddSingleton(HostAuthentication.Read(builder.Configuration));
        builder.Services.AddAuthentication("HostCredential").AddScheme<AuthenticationSchemeOptions,HostAuthentication>("HostCredential",_=>{});
        builder.Services.AddAuthorization();
        var accessor=new HttpContextAccessor();builder.Services.AddSingleton<IHttpContextAccessor>(accessor);
        if(configure is null)builder.Services.AddMaintenanceHost(builder.Configuration,()=>HostAuthentication.Identity(accessor.HttpContext),true);
        int Number(string key,int fallback)=>builder.Configuration[key] is {} text?int.Parse(text):fallback;
        builder.Services.AddSingleton(new StreamingOptions(Number("Streaming:Capacity",32),Number("Streaming:WriteTimeoutSeconds",10)));
        var app=builder.Build();
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
                var (status,code)=error switch{ApiProblemException p=>(p.Status,p.Code),BadHttpRequestException or JsonException or ArgumentException=>(400,"invalid_request"),OperationCanceledException=>(504,"operation_timeout"),_=>(503,"dependency_unavailable")};
                context.Response.StatusCode=status;await context.Response.WriteAsJsonAsync(new {error=code,correlationId=context.Items["correlation"]},context.RequestAborted);
            }
            finally{HostAccess.Correlation.Value=null;}
        });
        app.UseAuthentication();app.UseAuthorization();
        app.MapGet("/health/live",()=>Results.Ok(new{status="alive"}));
        app.MapGet("/health/ready",async(HttpContext c)=>
        {
            try
            {
                using var timeout=CancellationTokenSource.CreateLinkedTokenSource(c.RequestAborted);timeout.CancelAfter(TimeSpan.FromSeconds(3));
                foreach(var key in new[]{"operations","receiver"})
                {
                    var source=c.RequestServices.GetRequiredKeyedService<NpgsqlDataSource>(key);
                    await using var command=source.CreateCommand(key=="operations"?"SELECT EXISTS(SELECT 1 FROM information_schema.columns WHERE table_schema='operations' AND table_name='dispatch_attempts' AND column_name='next_reconciliation_at')":"SELECT to_regclass('dispatch_receiver.tickets') IS NOT NULL");
                    if(await command.ExecuteScalarAsync(timeout.Token) is not true)return Results.StatusCode(503);
                }
                await using var knowledge=c.RequestServices.GetRequiredService<NpgsqlDataSource>().CreateCommand("SELECT to_regclass('knowledge.chunks') IS NOT NULL");
                if(await knowledge.ExecuteScalarAsync(timeout.Token) is not true)return Results.StatusCode(503);
                return Results.Ok(new{status="ready"});
            }
            catch{return Results.StatusCode(503);}
        });
        if(app.Environment.IsDevelopment())app.MapOpenApi();
        MaintenanceEndpoints.Map(app);
        return app;
    }
}
