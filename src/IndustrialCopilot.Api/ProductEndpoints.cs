using IndustrialCopilot.Application.Abstractions.Usage;
using Microsoft.AspNetCore.Mvc;
using System.Globalization;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using IndustrialCopilot.Application.Ask;
using IndustrialCopilot.Application.Abstractions.Documents;
using IndustrialCopilot.Application.Abstractions.Documents.Models;
using IndustrialCopilot.Application.Knowledge;
using IndustrialCopilot.Infrastructure.Operations;
using Microsoft.AspNetCore.Http.Features;
namespace IndustrialCopilot.Api;

public sealed record AskStreamLimits(int TimeoutSeconds);

public static class ProductEndpoints
{
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
    public sealed record CreateConversation(Guid EquipmentId,Guid DocumentId,Guid ManualRevisionId);
    public sealed record QuestionRequest(string Question,int TopK=5);
    private static string Actor(HttpContext c)=>c.User.FindFirstValue(ClaimTypes.NameIdentifier)!;
    private static bool Can(HttpContext c,string permission,Guid equipment)=>HostAuthentication.Identity(c) is {} u && u.Permissions.Contains(permission)&&u.Equipment.Contains(equipment);
    private static IResult Fail(HttpContext c,int status,string code)=>Results.Json(ApiMessages.Failure(c,status,code),statusCode:status);
    public static void Map(WebApplication app)
    {
        var api=app.MapGroup("/api").RequireAuthorization();
        api.MapGet("/identity",(HttpContext c)=>{
            var u=HostAuthentication.Identity(c)!;
            return Results.Ok(new {u.Actor,permissions=u.Permissions.Order(),equipmentIds=u.Equipment.Order(),role=u.Permissions.Contains("approve")?"Supervisor":"Technician"});
        });
        api.MapPost("/conversations",async(CreateConversation request,HttpContext c,[FromServices] IConversationStore store,[FromServices] ProductCatalog catalog)=>{
            if(!Can(c,"read",request.EquipmentId))return Fail(c,403,"forbidden");
            if(!await catalog.ContainsAsync(request.EquipmentId,request.DocumentId,request.ManualRevisionId,c.RequestAborted))return Fail(c,404,"not_found");
            var culture=CultureInfo.CurrentUICulture.TwoLetterISOLanguageName=="ar"?"ar-EG":"en-US";
            return Results.Ok(await store.CreateAsync(Actor(c),request.EquipmentId,request.DocumentId,request.ManualRevisionId,culture,c.RequestAborted));
        });
        api.MapGet("/conversations",async(HttpContext c,[FromServices] IConversationStore store,int offset=0,int limit=20)=>{
            if(offset<0||offset>10000||limit is <1 or >50)return Fail(c,400,"invalid_request");
            var identity=HostAuthentication.Identity(c)!;
            return Results.Ok(await store.ListAsync(Actor(c),identity.Permissions.Contains("read")?identity.Equipment.ToArray():[],offset,limit,c.RequestAborted));
        });
        api.MapGet("/conversations/{id:guid}",async(Guid id,HttpContext c,[FromServices] IConversationStore store,long after=0,int limit=20)=>{
            if(after<0||limit is <1 or >50)return Fail(c,400,"invalid_request");
            var conversation=await store.GetAsync(Actor(c),id,c.RequestAborted);
            if(conversation is null||!Can(c,"read",conversation.EquipmentId))return Fail(c,404,"not_found");
            return Results.Ok(new{conversation,turns=await store.TurnsAsync(Actor(c),id,after,limit,c.RequestAborted)});
        });
        api.MapPost("/conversations/{id:guid}/ask",Ask);
        api.MapPost("/documents/{document:guid}/revisions/{revision:guid}/ingest",Ingest);
        api.MapGet("/documents/{document:guid}/revisions/{revision:guid}/ingestion",async(Guid document,Guid revision,Guid equipmentId,HttpContext c,[FromServices] ProductCatalog catalog,[FromServices] IIngestionReports reports)=>{
            if(!Can(c,"ingest",equipmentId))return Fail(c,403,"forbidden");
            if(!await catalog.ContainsAsync(equipmentId,document,revision,c.RequestAborted))return Fail(c,404,"not_found");
            return Results.Ok(await reports.ReadAsync(document,revision,c.RequestAborted));
        });
    }
    private static async Task Ask(Guid id,QuestionRequest body,HttpContext c,[FromServices] IConversationStore store,[FromServices] AskService service,[FromServices] IHistoryText history,[FromServices] AskStreamLimits limits)
    {
        AskQuestion question;
        try {question=new(body.Question,body.TopK);} catch(ArgumentException){await Fail(c,400,"invalid_request").ExecuteAsync(c);return;}
        var actor=Actor(c);var conversation=await store.GetAsync(actor,id,c.RequestAborted);
        if(conversation is null||!Can(c,"read",conversation.EquipmentId)){await Fail(c,404,"not_found").ExecuteAsync(c);return;}
        var correlation=(Guid)c.Items["correlation"]!;
        using var usage=new LlmCallScope(new UsageContext(actor,correlation,conversation.EquipmentId,Purpose:UsagePurpose.Ask));
        var safeQuestion=history.Sanitize(question.Text);
        var turn=await store.BeginAsync(actor,id,safeQuestion[..Math.Min(2000,safeQuestion.Length)],correlation,c.RequestAborted);
        if(turn is null){await Fail(c,409,"conversation_busy").ExecuteAsync(c);return;}
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(limits.TimeoutSeconds));
        using var linked=CancellationTokenSource.CreateLinkedTokenSource(c.RequestAborted,timeout.Token);
        var answer=new StringBuilder(); IReadOnlyList<AskCitation> citations=[]; var finalized=false;
        c.Response.ContentType="text/event-stream";c.Response.Headers["X-Accel-Buffering"]="no";
        // Each write is awaited: no producer queue and at most one bounded provider chunk in flight.
        async Task Send(string type,object data)
        {
            using var write=CancellationTokenSource.CreateLinkedTokenSource(linked.Token);write.CancelAfter(TimeSpan.FromSeconds(10));
            await c.Response.WriteAsync($"event: {type}\ndata: {JsonSerializer.Serialize(data,Json)}\n\n",write.Token);
            await c.Response.Body.FlushAsync(write.Token);
        }
        try
        {
            await Send("meta",new{conversationId=id,turnId=turn,correlationId=correlation,culture=conversation.Culture});
            await foreach(var item in service.StreamAsync(conversation,question,linked.Token))
            {
                if(item.Delta is {} delta)answer.Append(delta);
                if(item.Citations is {} sources)citations=sources;
                if(item.Type=="completed")
                {
                    finalized=await store.FinishAsync(actor,id,turn.Value,item.State!.Value,history.Sanitize(answer.ToString()),citations,linked.Token);
                    if(!finalized)throw new InvalidOperationException("Turn finalization conflict.");
                }
                await Send(item.Type,new{item.Delta,item.Citations,state=item.State?.ToString()});
            }
        }
        catch(Exception)
        {
            var state=c.RequestAborted.IsCancellationRequested?AskState.Cancelled:AskState.Failed;
            if(!finalized)
            {
                using var cleanup=new CancellationTokenSource(TimeSpan.FromSeconds(5));
                try{await store.FinishAsync(actor,id,turn.Value,state,"",[],cleanup.Token);}catch(Exception){/* Deadline makes an interrupted turn Failed on inspection. */}
            }
            if(!c.RequestAborted.IsCancellationRequested)
            {
                // Timeout token cannot be used to deliver its own safe terminal error.
                using var errorWrite=new CancellationTokenSource(TimeSpan.FromSeconds(2));
                try{await c.Response.WriteAsync($"event: error\ndata: {JsonSerializer.Serialize(ApiMessages.Failure(c,timeout.IsCancellationRequested?504:503,timeout.IsCancellationRequested?"operation_timeout":"ask_failed"),Json)}\n\n",errorWrite.Token);await c.Response.Body.FlushAsync(errorWrite.Token);}catch(Exception){}
            }
        }
    }
    private static async Task<IResult> Ingest(Guid document,Guid revision,Guid equipmentId,string filename,string title,int revisionNumber,
        HttpContext c,[FromServices] ProductCatalog catalog,[FromServices] ManualIngestionService ingestion,[FromServices] IIngestionReports reports)
    {
        if(!Can(c,"ingest",equipmentId))return Fail(c,403,"forbidden");
        if(document==Guid.Empty||revision==Guid.Empty||string.IsNullOrWhiteSpace(filename)||filename.Length>200||filename.IndexOfAny(['/', '\\', ':'])>=0||filename.Any(char.IsControl))return Fail(c,400,"invalid_filename");
        var media=c.Request.ContentType?.Split(';')[0];
        if(media is not ("text/plain" or "application/pdf") || (media=="text/plain"?!filename.EndsWith(".txt",StringComparison.OrdinalIgnoreCase):!filename.EndsWith(".pdf",StringComparison.OrdinalIgnoreCase)))return Fail(c,400,"unsupported_format");
        if(c.Request.ContentLength>16_000_000)return Fail(c,413,"payload_too_large");
        if(c.Features.Get<IHttpMaxRequestBodySizeFeature>() is {IsReadOnly:false} size)size.MaxRequestBodySize=16_000_000;
        DocumentProcessingRequest request;
        try{request=new(document,revision,media,new DocumentMetadata(title,filename,revisionNumber));}catch(ArgumentException){return Fail(c,400,"invalid_request");}
        if(!await catalog.RegisterAsync(equipmentId,document,revision,c.RequestAborted))return Fail(c,409,"document_scope_conflict");
        using var usage=new LlmCallScope(new UsageContext(Actor(c),(Guid)c.Items["correlation"]!,equipmentId,Purpose:UsagePurpose.Ingestion));
        using var bound=CancellationTokenSource.CreateLinkedTokenSource(c.RequestAborted);bound.CancelAfter(TimeSpan.FromMinutes(2));
        try
        {
            await ingestion.IngestAsync(request,c.Request.Body,bound.Token);
            return Results.Ok(await reports.ReadAsync(document,revision,bound.Token));
        }
        catch(DocumentInputException e){return Fail(c,e.Failure==IngestionFailure.InputTooLarge?413:422,"document_rejected");}
    }
}
