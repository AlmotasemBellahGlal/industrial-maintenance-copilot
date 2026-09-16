using IndustrialCopilot.Application.Ask;
using Microsoft.Extensions.Configuration;
namespace IndustrialCopilot.Infrastructure.AI;
/// <summary>Known deployment credentials and common sensitive text are removed from history copies.</summary>
public sealed class HistoryText(IConfiguration configuration) : IHistoryText
{
    public string Sanitize(string text)
    {
        foreach(var entry in configuration.GetSection("Authentication:Credentials").GetChildren())
            if(entry["Secret"] is {Length:>0} secret) text=text.Replace(secret,"[REDACTED]",StringComparison.Ordinal);
        return HostedDataBoundary.Redact(text,configuration["Llm:OpenAi:ApiKey"]);
    }
}
