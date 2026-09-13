using IndustrialCopilot.Application.Abstractions.Actions;
using IndustrialCopilot.Application.Abstractions.Approval;
using IndustrialCopilot.Application.Abstractions.Retrieval;
using IndustrialCopilot.Application.Abstractions.Safety;
using IndustrialCopilot.Application.Abstractions.Tracing;
using IndustrialCopilot.Application.Actions;
using IndustrialCopilot.Infrastructure.Knowledge;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace IndustrialCopilot.Infrastructure.Operations;

public static class TrustedActionRegistration
{
    /// <summary>Register after operational and knowledge services. Authorization and context are supplied
    /// by the authenticated host; adapter selection is explicit. No default external destination.</summary>
    public static IServiceCollection AddTrustedActions(this IServiceCollection services,IActionAuthorization authorization,
        ITrustedToolContextAccessor context,IExternalDispatch adapter)
    {
        ArgumentNullException.ThrowIfNull(authorization); ArgumentNullException.ThrowIfNull(context); ArgumentNullException.ThrowIfNull(adapter);
        services.AddSingleton(authorization); services.AddSingleton(context); services.AddSingleton(adapter);
        services.AddSingleton<IDispatchAttemptStore>(p=>new PostgresDispatchAttemptStore(p.GetRequiredKeyedService<NpgsqlDataSource>("operations"),authorization));
        services.AddSingleton<PostgresTrustedContext>(p=>new(p.GetRequiredKeyedService<NpgsqlDataSource>("operations"),authorization));
        services.AddSingleton<IEquipmentContextStore>(p=>p.GetRequiredService<PostgresTrustedContext>());
        services.AddSingleton<ISafetyVerificationService>(p=>p.GetRequiredService<PostgresTrustedContext>());
        services.AddSingleton<IWorkOrderApprovalService>(p=>new AuthorizedApprovalService(
            new PostgresWorkOrderApprovalService(p.GetRequiredService<PostgresWorkflowStore>(),p.GetRequiredService<OperationalAccessPolicy>(),TimeProvider.System),authorization));
        services.AddSingleton<DispatchCoordinator>();
        services.AddSingleton<TrustedToolExecutor>(p=>new(p.GetRequiredService<PostgresKnowledgeStore>(),p.GetRequiredService<IEquipmentContextStore>(),
            p.GetRequiredService<ISafetyPolicy>(),p.GetRequiredService<DispatchCoordinator>(),authorization,p.GetRequiredService<IRunTraceStore>()));
        services.AddSingleton<IRetrievalService,TrustedRetrievalService>();
        return services;
    }
}
