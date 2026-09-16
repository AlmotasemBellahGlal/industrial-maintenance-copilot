using System.Runtime.CompilerServices;
using IndustrialCopilot.Application.Abstractions.AI;
using IndustrialCopilot.Application.Abstractions.AI.Models;
using IndustrialCopilot.Application.Abstractions.Usage;
namespace IndustrialCopilot.Infrastructure.AI;

/// <summary>One ledger row per physical adapter call, beneath provider selection/fallback. No prompts or responses enter the ledger.</summary>
public sealed class AccountedLlmProvider(ILlmProvider inner, ILlmUsageStore store, UsagePricing pricing,
    string provider, string chatModel, string embeddingModel, BillingKind billing, TimeProvider? clock=null) : ILlmProvider
{
    private readonly TimeProvider time=clock??TimeProvider.System;
    private async Task<LlmUsageRecord> Begin(LlmOperation operation,CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var context=LlmCallScope.Current??new UsageContext("system",Guid.NewGuid());
        var record=new LlmUsageRecord(Guid.NewGuid(),context,provider,operation==LlmOperation.Embedding?embeddingModel:chatModel,operation,billing,time.GetUtcNow());
        await store.BeginAsync(record,ct);return record;
    }
    private async Task Finish(LlmUsageRecord record,UsageStatus status,TokenUsage? tokens,string? failure)
    {
        var rate=billing==BillingKind.Hosted?pricing.Find(provider,record.Model,record.StartedAt):null;
        var end=time.GetUtcNow(); if(end<record.StartedAt)end=record.StartedAt;
        using var cleanup=new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await store.FinishAsync(record with {Status=status,CompletedAt=end,Tokens=tokens,
            EstimatedCost=rate is not null && tokens is not null?rate.Estimate(tokens):null,
            PricingVersion=rate is not null && tokens is not null?rate.Version:null,FailureCode=failure},cleanup.Token);
    }
    private async Task<T> Execute<T>(LlmOperation operation,Func<Task<T>> call,Func<T,TokenUsage?> usage,CancellationToken ct)
    {
        var record=await Begin(operation,ct);TokenUsage? tokens=null;var status=UsageStatus.Failed;string? failure=null;
        try {var result=await call();tokens=usage(result);status=UsageStatus.Succeeded;return result;}
        catch(OperationCanceledException) when(ct.IsCancellationRequested){status=UsageStatus.Cancelled;failure="cancelled";throw;}
        catch(DependencyFailureException error){failure=error.Failure.ToString();throw;}
        catch {failure="operation_failed";throw;}
        finally {try {await Finish(record,status,tokens,failure);} catch when(status==UsageStatus.Cancelled) { /* Started remains an honest unknown if persistence is unavailable. */ }}
    }
    public Task<CompletionResponse> CompleteAsync(CompletionRequest request,CancellationToken ct)=>
        Execute(LlmOperation.Completion,()=>inner.CompleteAsync(request,ct),r=>r.Usage,ct);
    public Task<ToolCompletionResponse> CompleteWithToolsAsync(CompletionRequest request,IReadOnlyList<ToolDefinition> tools,CancellationToken ct)=>
        Execute(LlmOperation.ToolCompletion,()=>inner.CompleteWithToolsAsync(request,tools,ct),r=>r.Usage,ct);
    public Task<EmbeddingResult> GenerateEmbeddingsAsync(EmbeddingRequest request,CancellationToken ct)=>
        Execute(LlmOperation.Embedding,()=>inner.GenerateEmbeddingsAsync(request,ct),r=>r.Usage,ct);
    public async IAsyncEnumerable<StreamingChunk> StreamAsync(CompletionRequest request,[EnumeratorCancellation]CancellationToken ct)
    {
        var record=await Begin(LlmOperation.Streaming,ct);var status=UsageStatus.Incomplete;
        TokenUsage? tokens=null;string? failure="consumer_stopped";
        IAsyncEnumerator<StreamingChunk>? iterator=null;
        try
        {
            while(true)
            {
                StreamingChunk chunk;
                try
                {
                    ct.ThrowIfCancellationRequested();
                    iterator??=inner.StreamAsync(request,ct).GetAsyncEnumerator(ct);
                    if(!await iterator.MoveNextAsync())break;
                    chunk=iterator.Current;
                    if(chunk.IsCompleted){tokens=chunk.Usage;status=UsageStatus.Succeeded;failure=null;}
                }
                catch(OperationCanceledException) when(ct.IsCancellationRequested){status=UsageStatus.Cancelled;failure="cancelled";throw;}
                catch(DependencyFailureException error){status=UsageStatus.Failed;failure=error.Failure.ToString();throw;}
                catch {status=UsageStatus.Failed;failure="operation_failed";throw;}
                yield return chunk;
                if(chunk.IsCompleted)break;
            }
        }
        finally
        {
            try {if(iterator is not null)await iterator.DisposeAsync();}
            finally
            {
                if(status==UsageStatus.Incomplete && ct.IsCancellationRequested){status=UsageStatus.Cancelled;failure="cancelled";}
                try {await Finish(record,status,tokens,failure);} catch when(status==UsageStatus.Cancelled) { /* Preserve caller cancellation; Started remains inspectable. */ }
            }
        }
    }
}
