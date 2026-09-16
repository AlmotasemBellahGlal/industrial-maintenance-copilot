using IndustrialCopilot.Infrastructure.AI;
using Microsoft.Extensions.Configuration;
namespace IndustrialCopilot.Infrastructure.Tests.AI;
public class HistoryTextTests
{
    [Fact]public void HistoryRemovesKnownCredentialsAndSensitiveHumanTextWithoutTouchingIds()
    {
        var credential=new string('x',32);var configuration=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{["Authentication:Credentials:0:Secret"]=credential,["Llm:OpenAi:ApiKey"]="known-provider-credential"}).Build();
        var id=Guid.NewGuid().ToString();var safe=new HistoryText(configuration).Sanitize($"{id} {credential} known-provider-credential user@example.com password=private123");
        Assert.Contains(id,safe);Assert.DoesNotContain(credential,safe);Assert.DoesNotContain("known-provider",safe);Assert.DoesNotContain("user@example.com",safe);Assert.DoesNotContain("private123",safe);
    }
}
