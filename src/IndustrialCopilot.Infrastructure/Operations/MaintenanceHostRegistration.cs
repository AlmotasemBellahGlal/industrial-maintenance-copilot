using System.Text.Json;
using IndustrialCopilot.Application.Abstractions.Actions;
using IndustrialCopilot.Application.Abstractions.Approval;
using IndustrialCopilot.Application.Abstractions.Retrieval;
using IndustrialCopilot.Application.Abstractions.Safety;
using IndustrialCopilot.Application.Abstractions.Tracing;
using IndustrialCopilot.Application.Abstractions.Workflow;
using IndustrialCopilot.Application.Actions;
using IndustrialCopilot.Application.Reasoning;
using IndustrialCopilot.Infrastructure.AI;
using IndustrialCopilot.Infrastructure.Knowledge;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
namespace IndustrialCopilot.Infrastructure.Operations;

public static class MaintenanceHostRegistration
{
    private sealed record RequirementConfiguration(Guid Id,string Description,bool IsMandatory);
    private sealed record ProcedureConfiguration(IndustrialCopilot.Application.Abstractions.Agents.SymptomMatcher.EquipmentManualCandidate Candidate,
        IReadOnlyList<string> DiagnosticInstructions,IReadOnlyList<string> WorkInstructions,string WorkOrderDescription,
        IReadOnlyList<RequirementConfiguration> Requirements,bool ExplicitlyNoMandatoryRequirements=false);
    public static IServiceCollection AddMaintenanceHost(this IServiceCollection services,IConfiguration config,Func<HostIdentity?> identity,bool reasoning)
    {
        var file=config["Safety:ProcedureFile"];
        if(string.IsNullOrWhiteSpace(file)) throw new InvalidOperationException("Safety:ProcedureFile is required.");
        ApprovedMaintenanceProcedure[] procedures;
        try
        {
            var definitions=JsonSerializer.Deserialize<ProcedureConfiguration[]>(File.ReadAllText(file),new JsonSerializerOptions{PropertyNameCaseInsensitive=true})??throw new ArgumentException();
            procedures=definitions.Select(p=>new ApprovedMaintenanceProcedure(p.Candidate,p.DiagnosticInstructions,p.WorkInstructions,p.WorkOrderDescription,
                p.Requirements.Select(r=>new IndustrialCopilot.Domain.WorkOrders.Safety.SafetyPrerequisite(r.Id,r.Description,r.IsMandatory)).ToArray(),p.ExplicitlyNoMandatoryRequirements)).ToArray();
        }
        catch { throw new InvalidOperationException("Invalid reviewed procedure configuration."); }
        var safety=new ExactProcedureSafetyPolicy(procedures);
        services.AddSingleton<IReadOnlyList<ApprovedMaintenanceProcedure>>(Array.AsReadOnly(procedures));
        services.AddSingleton<ISafetyPolicy>(safety); services.AddSingleton<IExecutableSafetyPolicy>(safety);
        string Connection(string name)
        {
            try { var b=new NpgsqlConnectionStringBuilder(config.GetConnectionString(name)??throw new ArgumentException()){IncludeErrorDetail=false}; if(string.IsNullOrWhiteSpace(b.Host)||string.IsNullOrWhiteSpace(b.Database)) throw new ArgumentException(); return b.ConnectionString; }
            catch { throw new InvalidOperationException("Missing or invalid persistence configuration."); }
        }
        var operations=Connection("Operations"); var receiver=Connection("DispatchReceiver");
        if(config["Dispatch:Adapter"]!="PostgresInbox") throw new InvalidOperationException("An explicit supported dispatch adapter is required.");
        services.AddKeyedSingleton<NpgsqlDataSource>("operations",(_,_)=>NpgsqlDataSource.Create(operations));
        services.AddKeyedSingleton<NpgsqlDataSource>("receiver",(_,_)=>NpgsqlDataSource.Create(receiver));
        services.AddSingleton<PostgresWorkflowStore>(p=>new(p.GetRequiredKeyedService<NpgsqlDataSource>("operations")));
        services.AddSingleton<IWorkflowStore>(p=>p.GetRequiredService<PostgresWorkflowStore>());
        services.AddSingleton<HostAccess>(p=>new(identity,p.GetRequiredService<PostgresWorkflowStore>(),p.GetRequiredKeyedService<NpgsqlDataSource>("operations"),safety,procedures));
        services.AddSingleton<IActionAuthorization>(p=>p.GetRequiredService<HostAccess>());
        services.AddSingleton<ITrustedToolContextAccessor>(p=>p.GetRequiredService<HostAccess>());
        services.AddSingleton<OperationalAccessPolicy>(p=>p.GetRequiredService<HostAccess>());
        services.AddSingleton<IRunTraceStore>(p=>new PostgresRunTraceStore(p.GetRequiredKeyedService<NpgsqlDataSource>("operations"),p.GetRequiredService<HostAccess>()));
        services.AddSingleton<IWorkOrderApprovalService>(p=>new AuthorizedApprovalService(new PostgresWorkOrderApprovalService(p.GetRequiredService<PostgresWorkflowStore>(),p.GetRequiredService<HostAccess>(),TimeProvider.System),p.GetRequiredService<IActionAuthorization>()));
        services.AddSingleton<PostgresTrustedContext>(p=>new(p.GetRequiredKeyedService<NpgsqlDataSource>("operations"),p.GetRequiredService<IActionAuthorization>()));
        services.AddSingleton<IEquipmentContextStore>(p=>p.GetRequiredService<PostgresTrustedContext>());
        services.AddSingleton<ISafetyVerificationService>(p=>p.GetRequiredService<PostgresTrustedContext>());
        services.AddSingleton<IWorkflowInspection>(p=>new PostgresWorkflowInspection(p.GetRequiredKeyedService<NpgsqlDataSource>("operations")));
        services.AddSingleton<IDispatchAttemptStore>(p=>new PostgresDispatchAttemptStore(p.GetRequiredKeyedService<NpgsqlDataSource>("operations"),p.GetRequiredService<IActionAuthorization>()));
        services.AddSingleton<IExternalDispatch>(p=>new PostgresDispatchReceiver(p.GetRequiredKeyedService<NpgsqlDataSource>("receiver")));
        services.AddSingleton<IReconciliationDiscovery>(p=>new PostgresReconciliationDiscovery(p.GetRequiredKeyedService<NpgsqlDataSource>("operations")));
        services.AddSingleton<DispatchCoordinator>(); services.AddSingleton<ReconciliationBatch>(); services.AddSingleton<ExecutableReviewService>();
        services.AddSingleton<OperationalSchema>(p=>new(p.GetRequiredKeyedService<NpgsqlDataSource>("operations")));
        services.AddSingleton<DispatchReceiverSchema>(p=>new(p.GetRequiredKeyedService<NpgsqlDataSource>("receiver")));
        if(reasoning)
        {
            services.AddLlmProviders(config); services.AddKnowledgePipeline(config);
            services.AddSingleton<TrustedToolExecutor>(p=>new(p.GetRequiredService<PostgresKnowledgeStore>(),p.GetRequiredService<IEquipmentContextStore>(),safety,p.GetRequiredService<DispatchCoordinator>(),p.GetRequiredService<IActionAuthorization>(),p.GetRequiredService<IRunTraceStore>()));
            services.AddSingleton<IRetrievalService,TrustedRetrievalService>(); services.AddSingleton<MaintenanceOrchestrator>();
        }
        return services;
    }
    public static async Task MigrateAsync(IServiceProvider provider,bool knowledge,CancellationToken ct)
    {
        await provider.GetRequiredService<OperationalSchema>().ApplyAsync(ct);
        await provider.GetRequiredService<DispatchReceiverSchema>().ApplyAsync(ct);
        if(knowledge) await provider.GetRequiredService<KnowledgeSchema>().ApplyAsync(ct);
        var source=provider.GetRequiredKeyedService<NpgsqlDataSource>("operations");
        foreach(var procedure in provider.GetRequiredService<IReadOnlyList<ApprovedMaintenanceProcedure>>())
            await OperationalSql.Run(source,async(c,t)=>
            {
                var p=procedure.Candidate;
                await OperationalSql.Execute(c,t,"INSERT INTO operations.equipment VALUES(@equipment) ON CONFLICT DO NOTHING; INSERT INTO operations.manuals VALUES(@manual,@equipment) ON CONFLICT DO NOTHING; INSERT INTO operations.manual_revisions VALUES(@revision,@manual) ON CONFLICT DO NOTHING",ct,("equipment",p.EquipmentId),("manual",p.DocumentId),("revision",p.ManualRevisionId)); return true;
            },ct);
    }
}
