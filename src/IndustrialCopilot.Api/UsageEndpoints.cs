using IndustrialCopilot.Application.Abstractions.Usage;
using Microsoft.AspNetCore.Mvc;
namespace IndustrialCopilot.Api;

public static class UsageEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/api/usage",async(HttpContext c,[FromServices]ILlmUsageStore store,
            Guid? correlationId=null,Guid? runId=null,string? provider=null,DateTimeOffset? from=null,DateTimeOffset? until=null,int offset=0,int limit=50)=>{
            var identity=HostAuthentication.Identity(c)!;
            if(!identity.Permissions.Contains("read"))throw new ApiProblemException(403,"forbidden");
            var query=new UsageQuery(correlationId,runId,provider,from,until,offset,limit);query.Validate();
            return Results.Ok(await store.QueryAsync(identity.Actor,identity.Equipment.ToArray(),query,c.RequestAborted));
        }).RequireAuthorization();
    }
}
