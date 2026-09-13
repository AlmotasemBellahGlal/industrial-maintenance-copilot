using IndustrialCopilot.Application.Abstractions.AI.Models;
using IndustrialCopilot.Infrastructure.AI;

namespace IndustrialCopilot.Infrastructure.Tests.AI;

public class OpenAiEmbeddingTests
{
    private static EmbeddingRequest Request => new(["first", "second"]);

    [Fact]
    public async Task SendsBatchAndReconstructsOriginalOrderFromResponseIndexes()
    {
        var handler = Fixtures.Handler("""{"model":"actual-embedding","data":[{"index":1,"embedding":[3,4]},{"index":0,"embedding":[1,2]}]}""");
        using var provider = new OpenAiLlmProvider(Fixtures.Options(), handler);
        var result = await provider.GenerateEmbeddingsAsync(Request, default);
        Assert.Equal("https://api.openai.com/v1/embeddings", handler.Uri!.AbsoluteUri);
        Assert.Equal("test-embedding-model", handler.Body!["model"]!.GetValue<string>());
        Assert.Equal("float", handler.Body["encoding_format"]!.GetValue<string>());
        Assert.Equal(new[] { "first", "second" }, handler.Body["input"]!.AsArray().Select(v => v!.GetValue<string>()));
        Assert.Equal(new float[] { 1, 2 }, result.Vectors[0]);
        Assert.Equal(new float[] { 3, 4 }, result.Vectors[1]);
        Assert.Equal("actual-embedding", result.Model);
    }

    [Theory]
    [InlineData("[{\"index\":0,\"embedding\":[1]}]")]
    [InlineData("[{\"index\":0,\"embedding\":[1]},{\"index\":0,\"embedding\":[2]}]")]
    [InlineData("[{\"index\":-1,\"embedding\":[1]},{\"index\":1,\"embedding\":[2]}]")]
    [InlineData("[{\"index\":0,\"embedding\":[1]},{\"index\":2,\"embedding\":[2]}]")]
    [InlineData("[{\"index\":0,\"embedding\":[1]},{\"index\":1,\"embedding\":[2,3]}]")]
    [InlineData("[{\"index\":0,\"embedding\":[]},{\"index\":1,\"embedding\":[]}]")]
    [InlineData("[{\"index\":0,\"embedding\":[1e100]},{\"index\":1,\"embedding\":[2]}]")]
    [InlineData("[{\"embedding\":[1]},{\"index\":1,\"embedding\":[2]}]")]
    public async Task RejectsInvalidCountIndexesDimensionsOrValues(string data)
    {
        using var provider = new OpenAiLlmProvider(Fixtures.Options(), Fixtures.Handler("{\"model\":\"embed\",\"data\":" + data + "}"));
        var error = await Assert.ThrowsAsync<LlmProviderException>(() => provider.GenerateEmbeddingsAsync(Request, default));
        Assert.Equal(LlmProviderFailureKind.InvalidResponse, error.Kind);
    }
}
