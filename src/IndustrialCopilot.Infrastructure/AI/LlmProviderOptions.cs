namespace IndustrialCopilot.Infrastructure.AI;

public enum LlmProviderKind { OpenAi, Ollama }

/// <summary>A validated configuration snapshot. Embeddings have an independent provider
/// and never use chat fallback. Changing their model/space requires reindexing later.</summary>
public sealed class LlmProviderOptions
{
    public LlmProviderKind PrimaryProvider { get; }
    public LlmProviderKind? FallbackProvider { get; }
    public bool FallbackEnabled { get; }
    public LlmProviderKind EmbeddingProvider { get; }
    public OpenAiOptions? OpenAi { get; }
    public OllamaOptions? Ollama { get; }

    public LlmProviderOptions(LlmProviderKind primaryProvider, LlmProviderKind? fallbackProvider,
        bool fallbackEnabled, LlmProviderKind embeddingProvider, OpenAiOptions? openAi, OllamaOptions? ollama)
    {
        if (!Enum.IsDefined(primaryProvider) || !Enum.IsDefined(embeddingProvider) ||
            (fallbackProvider is { } fallback && !Enum.IsDefined(fallback)))
            throw new ArgumentException("Unknown LLM provider selection.");
        if (fallbackEnabled && (fallbackProvider is null || fallbackProvider == primaryProvider))
            throw new ArgumentException("Enabled fallback requires a distinct provider.");
        PrimaryProvider = primaryProvider;
        FallbackProvider = fallbackProvider;
        FallbackEnabled = fallbackEnabled;
        EmbeddingProvider = embeddingProvider;
        OpenAi = openAi;
        Ollama = ollama;
        if ((Requires(LlmProviderKind.OpenAi) && openAi is null) || (Requires(LlmProviderKind.Ollama) && ollama is null))
            throw new ArgumentException("Selected providers require validated settings.");
    }

    internal bool Requires(LlmProviderKind kind) => PrimaryProvider == kind || EmbeddingProvider == kind ||
        (FallbackEnabled && FallbackProvider == kind);
}
