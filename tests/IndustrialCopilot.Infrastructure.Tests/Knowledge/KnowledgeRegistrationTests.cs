using IndustrialCopilot.Application.Abstractions.Documents;
using IndustrialCopilot.Application.Abstractions.Indexing;
using IndustrialCopilot.Application.Abstractions.Retrieval;
using IndustrialCopilot.Application.Knowledge;
using IndustrialCopilot.Infrastructure.AI;
using IndustrialCopilot.Infrastructure.Knowledge;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace IndustrialCopilot.Infrastructure.Tests.Knowledge;

public class KnowledgeRegistrationTests
{
    private static IConfiguration Configuration(string revision = "weights-v1") => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["Knowledge:EmbeddingProfile"] = "manuals-v1", ["Knowledge:EmbeddingRevision"] = revision,
        ["Knowledge:Dimensions"] = "2", ["ConnectionStrings:Knowledge"] = "Host=localhost;Database=offline_test;Username=test"
    }).Build();
    private static ServiceCollection Services()
    {
        var services = new ServiceCollection();
        services.AddLlmProviders(new LlmProviderOptions(LlmProviderKind.Ollama, null, false, LlmProviderKind.Ollama, null,
            new OllamaOptions(new Uri("http://localhost:11434/"), "chat", "embed", TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1))));
        return services;
    }

    [Fact]
    public void RegistersPortsAndUseCaseWithoutOpeningDatabaseOrCallingModel()
    {
        using var host = Services().AddKnowledgePipeline(Configuration()).BuildServiceProvider();
        Assert.IsType<TextDocumentProcessor>(host.GetRequiredService<IDocumentProcessor>());
        Assert.Same(host.GetRequiredService<IKnowledgeIndex>(), host.GetRequiredService<IRetrievalService>());
        Assert.NotNull(host.GetRequiredService<ManualIngestionService>());
        Assert.Equal("embed", host.GetRequiredService<EmbeddingSpace>().Model);
    }

    [Fact]
    public void BindingChangesWithWeightsRevisionEvenWhenProfileAndDimensionsMatch()
    {
        using var one = Services().AddKnowledgePipeline(Configuration("weights-v1")).BuildServiceProvider();
        using var two = Services().AddKnowledgePipeline(Configuration("weights-v2")).BuildServiceProvider();
        var first = one.GetRequiredService<KnowledgeStoreOptions>(); var second = two.GetRequiredService<KnowledgeStoreOptions>();
        Assert.Equal(first.Space.Profile, second.Space.Profile);
        Assert.Equal(first.Space.Dimensions, second.Space.Dimensions);
        Assert.NotEqual(first.Binding, second.Binding);
    }

    [Fact]
    public void MissingConfigurationFailsBeforeConnectionsAreCreated()
    {
        Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddKnowledgePipeline(Configuration()));
        Assert.Throws<InvalidOperationException>(() => Services().AddKnowledgePipeline(new ConfigurationBuilder().Build()));
    }
}
