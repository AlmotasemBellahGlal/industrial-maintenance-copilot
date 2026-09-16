using IndustrialCopilot.Application.Abstractions.Usage;
using System.Globalization;
using IndustrialCopilot.Application.Abstractions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace IndustrialCopilot.Infrastructure.AI;

public static class DependencyInjection
{
    /// <summary>Reads the Llm section once and validates before registering services.
    /// Supply Llm:OpenAi:ApiKey using environment variables/user secrets, never source control.
    /// Settings changes require rebuilding the host; no live mutable options are retained.</summary>
    public static IServiceCollection AddLlmProviders(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        var section = configuration.GetSection("Llm");
        var primary = Provider(section, "PrimaryProvider");
        var embedding = Provider(section, "EmbeddingProvider");
        var enabledText = section["FallbackEnabled"];
        if (!bool.TryParse(enabledText, out var enabled)) throw Invalid("FallbackEnabled");
        LlmProviderKind? fallback = string.IsNullOrWhiteSpace(section["FallbackProvider"]) ? null : Provider(section, "FallbackProvider");
        bool Requires(LlmProviderKind kind) => primary == kind || embedding == kind || (enabled && fallback == kind);
        OpenAiOptions? openAi = null;
        OllamaOptions? ollama = null;
        try
        {
            if (Requires(LlmProviderKind.OpenAi))
            {
                var settings = section.GetSection("OpenAi");
                openAi = new(Endpoint(settings), Required(settings, "ChatModel"), Required(settings, "EmbeddingModel"),
                    Required(settings, "ApiKey"), Seconds(settings, "RequestTimeoutSeconds"), Seconds(settings, "StreamTimeoutSeconds"));
            }
            if (Requires(LlmProviderKind.Ollama))
            {
                var settings = section.GetSection("Ollama");
                ollama = new(Endpoint(settings), Required(settings, "ChatModel"), Required(settings, "EmbeddingModel"),
                    Seconds(settings, "RequestTimeoutSeconds"), Seconds(settings, "StreamTimeoutSeconds"));
            }
            var options = new LlmProviderOptions(primary, fallback, enabled, embedding, openAi, ollama);
            return services.AddLlmProviders(options);
        }
        catch (ArgumentException) { throw Invalid("provider settings"); }
    }

    public static IServiceCollection AddLlmProviders(this IServiceCollection services, LlmProviderOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        if (services.Any(service => service.ServiceType == typeof(ILlmProvider)))
            throw new InvalidOperationException("ILlmProvider is already registered.");
        services.AddSingleton(options);
        if (options.Requires(LlmProviderKind.OpenAi))
            services.AddSingleton<OpenAiLlmProvider>(_ => new(options.OpenAi!));
        if (options.Requires(LlmProviderKind.Ollama))
            services.AddSingleton<OllamaLlmProvider>(_ => new(options.Ollama!));
        services.AddSingleton<ILlmProvider>(host =>
        {
            ILlmProvider Resolve(LlmProviderKind kind) => kind switch
            {
                LlmProviderKind.OpenAi => host.GetRequiredService<OpenAiLlmProvider>(),
                LlmProviderKind.Ollama => host.GetRequiredService<OllamaLlmProvider>(),
                _ => throw Invalid("provider selection")
            };
            ILlmProvider Account(LlmProviderKind kind)
            {
                var raw=Resolve(kind);var store=host.GetService<ILlmUsageStore>();if(store is null)return raw;
                var chat=kind==LlmProviderKind.OpenAi?options.OpenAi!.ChatModel:options.Ollama!.ChatModel;
                var embedding=kind==LlmProviderKind.OpenAi?options.OpenAi!.EmbeddingModel:options.Ollama!.EmbeddingModel;
                return new AccountedLlmProvider(raw,store,host.GetRequiredService<UsagePricing>(),kind.ToString(),chat,embedding,
                    kind==LlmProviderKind.OpenAi?BillingKind.Hosted:BillingKind.Local);
            }
            return new ConfiguredLlmProvider(Account(options.PrimaryProvider),
                options.FallbackEnabled ? Account(options.FallbackProvider!.Value) : null, Account(options.EmbeddingProvider));
        });
        return services;
    }

    private static LlmProviderKind Provider(IConfiguration section, string name) => section[name] switch
    {
        "OpenAi" => LlmProviderKind.OpenAi,
        "Ollama" => LlmProviderKind.Ollama,
        _ => throw Invalid(name)
    };

    private static string Required(IConfiguration section, string name) =>
        !string.IsNullOrWhiteSpace(section[name]) ? section[name]! : throw Invalid(name);

    private static Uri Endpoint(IConfiguration section) =>
        Uri.TryCreate(Required(section, "Endpoint"), UriKind.Absolute, out var uri) ? uri : throw Invalid("Endpoint");

    private static TimeSpan Seconds(IConfiguration section, string name)
    {
        if (!double.TryParse(section[name], NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) ||
            !double.IsFinite(seconds) || seconds <= 0 || seconds > (uint.MaxValue - 1) / 1000d) throw Invalid(name);
        return TimeSpan.FromSeconds(seconds);
    }

    // Never echo configuration values: even an endpoint or malformed timeout may contain a secret.
    private static InvalidOperationException Invalid(string field) => new($"Invalid or missing Llm configuration: {field}.");
}
