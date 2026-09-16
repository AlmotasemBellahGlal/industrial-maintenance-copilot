using IndustrialCopilot.Application.Abstractions.Agents;
using IndustrialCopilot.Application.Abstractions.AI.Models;
using IndustrialCopilot.Application.Abstractions.Tracing.Models;
namespace IndustrialCopilot.Application.Abstractions.Usage;

public enum LlmOperation { Completion, ToolCompletion, Streaming, Embedding }
public enum UsagePurpose { Unspecified, Agent, Ask, GroundedFallback, Ingestion }
public enum UsageStatus { Started, Succeeded, Failed, Cancelled, Incomplete }
public enum BillingKind { Hosted, Local, Synthetic }
public sealed record UsageContext(string Actor, Guid CorrelationId, Guid? EquipmentId = null,
    Guid? RunId = null, Guid? ExecutionId = null, AgentRole? Agent = null, Guid? StepId = null,
    UsagePurpose Purpose = UsagePurpose.Unspecified);

/// <summary>Trusted in-process call context; never populated with model arguments or a client-supplied actor.</summary>
public sealed class LlmCallScope : IDisposable
{
    private static readonly AsyncLocal<LlmCallScope?> ambient = new();
    private readonly LlmCallScope? previous;
    public static UsageContext? Current => ambient.Value?.Context;
    public static bool OwnsRetries => ambient.Value?.ManagedRetries ?? false;
    private UsageContext? Context { get; }
    private bool ManagedRetries { get; }
    public LlmCallScope(UsageContext? context, bool? managedRetries = null)
    {
        previous = ambient.Value; Context = context; ManagedRetries = managedRetries ?? OwnsRetries;
        ambient.Value = this;
    }
    public void Dispose() => ambient.Value = previous;
}

/// <summary>One physical provider attempt. Started after a crash means unknown completion, never zero usage.</summary>
public sealed record LlmUsageRecord(Guid CallId, UsageContext Context, string Provider, string Model,
    LlmOperation Operation, BillingKind Billing, DateTimeOffset StartedAt, UsageStatus Status = UsageStatus.Started,
    DateTimeOffset? CompletedAt = null, TokenUsage? Tokens = null, MonetaryCost? EstimatedCost = null,
    string? PricingVersion = null, string? FailureCode = null);
public sealed record UsageQuery(Guid? CorrelationId = null, Guid? RunId = null, string? Provider = null,
    DateTimeOffset? From = null, DateTimeOffset? Until = null, int Offset = 0, int Limit = 50)
{
    public void Validate()
    {
        if (CorrelationId == Guid.Empty || RunId == Guid.Empty || Offset is < 0 or > 10000 || Limit is < 1 or > 100
            || From == default(DateTimeOffset) || Until == default(DateTimeOffset) || From > Until
            || (Provider is not null && (string.IsNullOrWhiteSpace(Provider) || Provider.Length > 64)))
            throw new ArgumentException("Invalid usage query.");
    }
}
public sealed record UsageSummary(long Calls, long KnownTokenCalls, long? PromptTokens, long? CompletionTokens,
    long? TotalTokens, long UnavailableTokenCalls, long UnpricedHostedCalls, long NonHostedCalls,
    IReadOnlyList<MonetaryCost> KnownEstimatedCosts);
public sealed record UsagePage(IReadOnlyList<LlmUsageRecord> Records, UsageSummary Summary);
public interface ILlmUsageStore
{
    Task BeginAsync(LlmUsageRecord record, CancellationToken ct);
    Task FinishAsync(LlmUsageRecord record, CancellationToken ct);
    /// <summary>Actor and equipment permissions come from the trusted host, never query string identities.</summary>
    Task<UsagePage> QueryAsync(string actor, IReadOnlyCollection<Guid> equipment, UsageQuery query, CancellationToken ct);
}
