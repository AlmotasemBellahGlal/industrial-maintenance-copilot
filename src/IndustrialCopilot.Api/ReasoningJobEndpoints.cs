using System.Text.Json;
using IndustrialCopilot.Application.Abstractions.Jobs;

namespace IndustrialCopilot.Api;

public static class ReasoningJobEndpoints
{
    private static IReasoningJobStore Store(HttpContext c)=>c.RequestServices.GetRequiredService<IReasoningJobStore>();
    private static async Task<ReasoningJobSnapshot> Read(Guid id,HttpContext c,string permission="read")
    {
        HttpValidation.Id(id);
        var job=await Store(c).GetAsync(id,c.RequestAborted)??throw new ApiProblemException(404,"not_found");
        var actor=HostAuthentication.Identity(c)??throw new ApiProblemException(401,"unauthenticated");
        if(!actor.Permissions.Contains(permission)||!actor.Permissions.Contains("read")||!actor.Equipment.Contains(job.EquipmentId))
            throw new ApiProblemException(403,"forbidden");
        return job;
    }
    private static object View(ReasoningJobSnapshot j)=>new {j.JobId,j.MaintenanceRunId,j.EquipmentId,j.CorrelationId,
        status=j.Status.ToString(),phase=j.Phase.ToString(),j.CreatedAt,j.UpdatedAt,j.CancellationRequested,j.Version,j.Attempts,
        result=j.Result is {} r?new {outcome=r.Outcome.ToString(),r.RunId,r.WorkOrderId,r.TraceComplete,r.Narrative}:null,
        j.FailureCode,statusUrl=$"/api/jobs/{j.JobId}",progressUrl=$"/api/jobs/{j.JobId}/events"};
    public static void Map(WebApplication app)
    {
        var routes=app.MapGroup("/api/jobs").RequireAuthorization();
        routes.MapPost("/",async(StartRunRequest body,HttpContext c)=>
        {
            var request=await MaintenanceEndpoints.Prepare(body,c);
            var submission=new ReasoningJobSubmission(HostAuthentication.Identity(c)!.Actor,c.Request.Headers["Idempotency-Key"].ToString(),request);
            ReasoningJobSnapshot job;
            try { job=await Store(c).SubmitAsync(submission,c.RequestAborted); }
            catch(JobSubmissionConflictException) {throw new ApiProblemException(409,"submission_conflict");}
            c.Response.Headers.Location=$"/api/jobs/{job.JobId}";
            return Results.Json(View(job),statusCode:202);
        });
        routes.MapGet("/{id:guid}",async(Guid id,HttpContext c)=>View(await Read(id,c)));
        routes.MapPost("/{id:guid}/cancel",async(Guid id,HttpContext c)=>
        {
            await Read(id,c,"start");
            try {return Results.Json(View((await Store(c).RequestCancellationAsync(id,c.RequestAborted))!));}
            catch(JobCancellationConflictException) {throw new ApiProblemException(409,"cancellation_conflict");}
        });
        routes.MapGet("/{id:guid}/events",Stream).Produces(200,contentType:"text/event-stream");
    }
    private static async Task Stream(Guid id,HttpContext c)
    {
        var job=await Read(id,c);long cursor=0;
        var last=c.Request.Headers["Last-Event-ID"].ToString();
        if(last.Length>0 && (!long.TryParse(last,out cursor)||cursor<0||cursor>job.Version))throw new ArgumentException("Invalid progress cursor.");
        var timeout=c.RequestServices.GetRequiredService<StreamingOptions>().WriteTimeout;
        c.Response.ContentType="text/event-stream";c.Response.Headers.CacheControl="no-store";c.Response.Headers["X-Accel-Buffering"]="no";
        await Write("Job",View(job));
        while(true)
        {
            // Snapshot first, then drain bounded event pages through its version before announcing completion.
            job=await Read(id,c);
            var events=await Store(c).ReadProgressAsync(id,cursor,64,c.RequestAborted);
            foreach(var e in events)
            {
                await Write(e.Progress.Kind.ToString(),new {job.CorrelationId,e.ExecutionId,progress=e.Progress},e.Sequence);
                cursor=e.Sequence;
            }
            if(events.Count==64)continue;
            if(job.IsTerminal)
            {
                if(job.Result is {} result)
                    await Write("Result",new {result.RunId,result.WorkOrderId,ExecutionId=job.Attempts.LastOrDefault()?.ExecutionId,job.CorrelationId,Outcome=result.Outcome.ToString(),result.Narrative});
                await Write("Job",View(job));return;
            }
            await Write("Job",View(job));
            await Task.Delay(TimeSpan.FromSeconds(1),c.RequestAborted);
        }
        async Task Write(string kind,object value,long? sequence=null)
        {
            using var write=CancellationTokenSource.CreateLinkedTokenSource(c.RequestAborted);write.CancelAfter(timeout);
            await c.Response.WriteAsync((sequence.HasValue?$"id: {sequence}\n":"")+"event: "+kind+"\ndata: "+JsonSerializer.Serialize(value)+"\n\n",write.Token);
            await c.Response.Body.FlushAsync(write.Token);
        }
    }
}
