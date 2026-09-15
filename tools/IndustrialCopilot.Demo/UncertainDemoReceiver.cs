using IndustrialCopilot.Application.Abstractions.Actions;
using IndustrialCopilot.Infrastructure.Operations;

namespace IndustrialCopilot.Demo;

/// <summary>Explicit failure simulation: durable receiver acceptance with a lost acknowledgement.</summary>
internal sealed class UncertainDemoReceiver(PostgresDispatchReceiver receiver) : IExternalDispatch
{
    public async Task<ExternalDispatchResult> SendAsync(DispatchAttempt attempt,CancellationToken ct)
    {
        await receiver.SendAsync(attempt,ct);
        return ExternalDispatchResult.Uncertain();
    }
    public Task<ExternalDispatchResult> ReconcileAsync(Guid key,CancellationToken ct)=>receiver.ReconcileAsync(key,ct);
}
