using IndustrialCopilot.Application.Abstractions.Approval;
using IndustrialCopilot.Application.Abstractions.Tracing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace IndustrialCopilot.Infrastructure.Operations;

public static class OperationalRegistration
{
    /// <summary>Host must supply a real policy; no implicit authorization or automatic migration.</summary>
    public static IServiceCollection AddOperationalPersistence(this IServiceCollection services,IConfiguration configuration,OperationalAccessPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(services); ArgumentNullException.ThrowIfNull(configuration); ArgumentNullException.ThrowIfNull(policy);
        var text=configuration.GetConnectionString("Operations");
        if(string.IsNullOrWhiteSpace(text)) throw new InvalidOperationException("ConnectionStrings:Operations is required.");
        string connection;
        try
        {
            var builder=new NpgsqlConnectionStringBuilder(text) { IncludeErrorDetail=false };
            if(string.IsNullOrWhiteSpace(builder.Host) || string.IsNullOrWhiteSpace(builder.Database)) throw new ArgumentException();
            connection=builder.ConnectionString;
        }
        catch(ArgumentException) { throw new InvalidOperationException("Invalid operational database configuration."); }
        // Dedicated keyed pool prevents collision with the knowledge-store connection.
        services.AddKeyedSingleton<NpgsqlDataSource>("operations",(_,_)=>NpgsqlDataSource.Create(connection));
        services.AddSingleton(policy);
        services.AddSingleton<OperationalSchema>(p=>new(p.GetRequiredKeyedService<NpgsqlDataSource>("operations")));
        services.AddSingleton<PostgresWorkflowStore>(p=>new(p.GetRequiredKeyedService<NpgsqlDataSource>("operations")));
        services.AddSingleton<IWorkOrderApprovalService>(p=>new PostgresWorkOrderApprovalService(p.GetRequiredService<PostgresWorkflowStore>(),policy,TimeProvider.System));
        services.AddSingleton<IRunTraceStore>(p=>new PostgresRunTraceStore(p.GetRequiredKeyedService<NpgsqlDataSource>("operations"),policy));
        return services;
    }
}
