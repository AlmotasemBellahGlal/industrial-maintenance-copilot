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
        Assert.Null(result.Usage);
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
    [Theory][InlineData(7,7,true)][InlineData(-1,-1,false)][InlineData(7,8,false)]
    public async Task MapsOnlyValidSuppliedEmbeddingUsage(int prompt,int total,bool valid)
    {
        var json=System.Text.Json.JsonSerializer.Serialize(new{model="embed",data=new[]{new{index=0,embedding=new[]{1f}}},usage=new{prompt_tokens=prompt,total_tokens=total}});
        using var provider=new OpenAiLlmProvider(Fixtures.Options(),Fixtures.Handler(json));
        if(valid)Assert.Equal(new TokenUsage(7,0,7),(await provider.GenerateEmbeddingsAsync(new(["one"]),default)).Usage);
        else Assert.Equal(LlmProviderFailureKind.InvalidResponse,(await Assert.ThrowsAsync<LlmProviderException>(()=>provider.GenerateEmbeddingsAsync(new(["one"]),default))).Kind);
    }
}
