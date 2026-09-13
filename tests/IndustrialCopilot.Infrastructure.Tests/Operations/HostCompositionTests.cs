using System.Text.Json;
using IndustrialCopilot.Application.Abstractions.Actions;
using IndustrialCopilot.Application.Abstractions.Safety;
using IndustrialCopilot.Application.Actions;
using IndustrialCopilot.Infrastructure.Operations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
namespace IndustrialCopilot.Infrastructure.Tests.Operations;
public class HostCompositionTests
{
    [Fact]
    public async Task ReviewedConfigurationBuildsWorkerGraphWithoutLlmAndMissingIdentityDenies()
    {
        var equipment=Guid.NewGuid();var manual=Guid.NewGuid();var revision=Guid.NewGuid();var requirement=Guid.NewGuid();var path=Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path,JsonSerializer.Serialize(new[]{new{Candidate=new{EquipmentId=equipment,EquipmentName="pump",DocumentId=manual,ManualRevisionId=revision},DiagnosticInstructions=new[]{"inspect"},WorkInstructions=new[]{"repair"},WorkOrderDescription="reviewed",Requirements=new[]{new{Id=requirement,Description="isolate",IsMandatory=true}}}}));
            var configuration=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{["Safety:ProcedureFile"]=path,["ConnectionStrings:Operations"]="Host=localhost;Database=not_contacted",["ConnectionStrings:DispatchReceiver"]="Host=localhost;Database=not_contacted",["Dispatch:Adapter"]="PostgresInbox"}).Build();
            var services=new ServiceCollection();services.AddMaintenanceHost(configuration,()=>null,false);
            await using var provider=services.BuildServiceProvider(new ServiceProviderOptions{ValidateOnBuild=true,ValidateScopes=true});
            Assert.NotNull(provider.GetRequiredService<ReconciliationBatch>());
            Assert.False(await provider.GetRequiredService<IActionAuthorization>().AuthorizeAsync("claimed-supervisor",TrustedAction.ReadEquipment,equipment,default));
            var safety=await provider.GetRequiredService<IExecutableSafetyPolicy>().AssessAsync(new(equipment,manual,revision,"noise","reviewed",[new(1,"repair")]),default);
            Assert.Equal(requirement,Assert.Single(safety.Requirements).Id);
        }
        finally{File.Delete(path);}
    }
}
