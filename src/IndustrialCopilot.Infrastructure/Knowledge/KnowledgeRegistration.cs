using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using IndustrialCopilot.Application.Abstractions.Documents;
using IndustrialCopilot.Application.Abstractions.Indexing;
using IndustrialCopilot.Application.Abstractions.Retrieval;
using IndustrialCopilot.Application.Knowledge;
using IndustrialCopilot.Infrastructure.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace IndustrialCopilot.Infrastructure.Knowledge;

public static class KnowledgeRegistration
{
    public static IServiceCollection AddKnowledgePipeline(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        var llm = services.LastOrDefault(d => d.ServiceType == typeof(LlmProviderOptions))?.ImplementationInstance as LlmProviderOptions
            ?? throw new InvalidOperationException("Register validated LLM providers before the knowledge pipeline.");
        var section = configuration.GetSection("Knowledge");
        var model = llm.EmbeddingProvider == LlmProviderKind.OpenAi ? llm.OpenAi!.EmbeddingModel : llm.Ollama!.EmbeddingModel;
        var endpoint = llm.EmbeddingProvider == LlmProviderKind.OpenAi ? llm.OpenAi!.Endpoint : llm.Ollama!.Endpoint;
        var profile = Required(section, "EmbeddingProfile");
        // Deployment-owned immutable weights/preprocessing revision, not the display profile.
        var revision = Required(section, "EmbeddingRevision");
        var binding = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{llm.EmbeddingProvider}\n{endpoint}\n{model}\n{revision}")));
        try
        {
            var space = new EmbeddingSpace(profile, model, Number(section, "Dimensions"));
            var options = new KnowledgeStoreOptions(space, binding, Number(section, "HybridCandidates", 100));
            var processor = new TextDocumentProcessor(Number(section, "ChunkSize", 1200), Number(section, "Overlap", 200));
            var connection = new NpgsqlConnectionStringBuilder(Required(configuration.GetSection("ConnectionStrings"), "Knowledge")) { IncludeErrorDetail = false };
            if (string.IsNullOrWhiteSpace(connection.Host) || string.IsNullOrWhiteSpace(connection.Database)) throw new ArgumentException();
            services.AddSingleton(space);
            services.AddSingleton(options);
            services.AddSingleton<IDocumentProcessor>(processor);
            services.AddSingleton(_ => NpgsqlDataSource.Create(connection.ConnectionString));
            services.AddSingleton<KnowledgeSchema>();
            services.AddSingleton<PostgresKnowledgeStore>();
            services.AddSingleton<IKnowledgeIndex>(p => p.GetRequiredService<PostgresKnowledgeStore>());
            services.AddSingleton<IRetrievalService>(p => p.GetRequiredService<PostgresKnowledgeStore>());
            services.AddSingleton<ManualIngestionService>();
            return services;
        }
        catch (ArgumentException) { throw new InvalidOperationException("Invalid knowledge pipeline configuration."); }
    }

    private static string Required(IConfiguration section, string name) =>
        !string.IsNullOrWhiteSpace(section[name]) ? section[name]! : throw new InvalidOperationException($"Missing knowledge configuration: {name}.");
    private static int Number(IConfiguration section, string name, int? fallback = null)
    {
        if (section[name] is null && fallback is { } value) return value;
        if (!int.TryParse(section[name], NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
            throw new InvalidOperationException($"Invalid knowledge configuration: {name}.");
        return number;
    }
}
