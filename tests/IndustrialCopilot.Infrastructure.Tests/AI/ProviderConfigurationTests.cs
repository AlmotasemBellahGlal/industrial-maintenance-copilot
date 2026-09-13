using IndustrialCopilot.Application.Abstractions.AI;
using IndustrialCopilot.Infrastructure.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace IndustrialCopilot.Infrastructure.Tests.AI;

public class ProviderConfigurationTests
{
    private static Dictionary<string, string?> LocalConfiguration() => new()
    {
        ["Llm:PrimaryProvider"] = "Ollama", ["Llm:EmbeddingProvider"] = "Ollama", ["Llm:FallbackEnabled"] = "false",
        ["Llm:Ollama:Endpoint"] = "http://localhost:11434/", ["Llm:Ollama:ChatModel"] = "llama3.1:8b",
        ["Llm:Ollama:EmbeddingModel"] = "embeddinggemma", ["Llm:Ollama:RequestTimeoutSeconds"] = "60",
        ["Llm:Ollama:StreamTimeoutSeconds"] = "120"
    };

    [Fact]
    public void LocalOnlyConfigurationNeedsNoOpenAiCredentials()
    {
        var services = new ServiceCollection().AddLlmProviders(new ConfigurationBuilder().AddInMemoryCollection(LocalConfiguration()).Build());
        using var host = services.BuildServiceProvider();
        Assert.IsType<ConfiguredLlmProvider>(host.GetRequiredService<ILlmProvider>());
        Assert.Null(host.GetService<OpenAiLlmProvider>());
        Assert.Same(host.GetRequiredService<ILlmProvider>(), host.GetRequiredService<ILlmProvider>());
        Assert.Equal(LlmProviderKind.Ollama, host.GetRequiredService<LlmProviderOptions>().EmbeddingProvider);
    }

    [Theory]
    [InlineData("Llm:PrimaryProvider", "unknown")]
    [InlineData("Llm:EmbeddingProvider", "0")]
    [InlineData("Llm:FallbackEnabled", "maybe")]
    [InlineData("Llm:FallbackEnabled", "true")]
    [InlineData("Llm:Ollama:Endpoint", "http://remote.example/")]
    [InlineData("Llm:Ollama:RequestTimeoutSeconds", "NaN")]
    [InlineData("Llm:Ollama:StreamTimeoutSeconds", "0")]
    [InlineData("Llm:Ollama:EmbeddingModel", "")]
    public void RejectsInvalidConfigurationAtRegistration(string key, string value)
    {
        var config = LocalConfiguration();
        config[key] = value;
        var services = new ServiceCollection();
        Assert.Throws<InvalidOperationException>(() => services.AddLlmProviders(new ConfigurationBuilder().AddInMemoryCollection(config).Build()));
        Assert.Empty(services);
    }

    [Fact]
    public void RejectsMissingHostedCredentialsAndDuplicateFallbackWithoutLeakingValues()
    {
        var config = LocalConfiguration();
        config["Llm:PrimaryProvider"] = "OpenAi";
        config["Llm:OpenAi:Endpoint"] = "https://secret.example/";
        var error = Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddLlmProviders(new ConfigurationBuilder().AddInMemoryCollection(config).Build()));
        Assert.DoesNotContain("secret.example", error.ToString());
        Assert.Throws<ArgumentException>(() => new LlmProviderOptions(LlmProviderKind.Ollama, LlmProviderKind.Ollama, true, LlmProviderKind.Ollama, null, OllamaProviderTests.Options));
    }

    [Fact]
    public async Task DiSelectsConfiguredPrimaryFallbackAndIndependentEmbeddingProvider()
    {
        var options = new LlmProviderOptions(LlmProviderKind.OpenAi, LlmProviderKind.Ollama, true, LlmProviderKind.Ollama,
            Fixtures.Options(), OllamaProviderTests.Options);
        var services = new ServiceCollection().AddLlmProviders(options);
        var openAiHandler = new FakeHandler(_ => Task.FromResult(Fixtures.Json("", System.Net.HttpStatusCode.ServiceUnavailable)));
        var ollamaHandler = new FakeHandler(_ => Task.FromResult(Fixtures.Json(Fixtures.Completion)));
        // Override concrete transports; the production selector/registration remains real.
        services.AddSingleton<OpenAiLlmProvider>(_ => new(Fixtures.Options(), openAiHandler));
        services.AddSingleton<OllamaLlmProvider>(_ => new(OllamaProviderTests.Options, ollamaHandler));
        using var host = services.BuildServiceProvider();
        await host.GetRequiredService<ILlmProvider>().CompleteAsync(Fixtures.Query, default);
        Assert.Equal(1, openAiHandler.Sends);
        Assert.Equal(1, ollamaHandler.Sends);
        Assert.Equal(LlmProviderKind.Ollama, host.GetRequiredService<LlmProviderOptions>().EmbeddingProvider);
    }

    [Fact]
    public void RejectsDuplicateRegistrationAndUnsafeLocalEndpoint()
    {
        var options = new LlmProviderOptions(LlmProviderKind.Ollama, null, false, LlmProviderKind.Ollama, null, OllamaProviderTests.Options);
        var services = new ServiceCollection().AddLlmProviders(options);
        Assert.Throws<InvalidOperationException>(() => services.AddLlmProviders(options));
        Assert.Throws<ArgumentException>(() => new OllamaOptions(new("http://remote.example/"), "chat", "embed", TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1)));
    }
}
