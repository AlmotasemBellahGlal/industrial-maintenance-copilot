using IndustrialCopilot.Application.Abstractions.AI.Models;
using IndustrialCopilot.Infrastructure.AI;
namespace IndustrialCopilot.Infrastructure.Tests.AI;

public class SecurityBoundaryTests
{
    [Theory]
    [InlineData("person@example.test")]
    [InlineData("+201012345678")]
    [InlineData("202-555-0123")]
    [InlineData("123-45-6789")]
    [InlineData("national_id=12345678901234")]
    [InlineData("password=synthetic-password")]
    [InlineData("Bearer synthetic-token-12345678")]
    public async Task HostedPayloadRedactsSensitiveTextWithoutMutatingCaller(string sensitive)
    {
        var handler=Fixtures.Handler(Fixtures.Completion);
        using var provider=new OpenAiLlmProvider(Fixtures.Options(),handler);
        var original=new LlmMessage(LlmRole.User,"Contact "+sensitive);
        await provider.CompleteAsync(new([original]),default);
        Assert.DoesNotContain(sensitive,handler.Body!["messages"]![0]!["content"]!.GetValue<string>());
        Assert.Contains(sensitive,original.Content);
    }
    [Fact]
    public void GuidProvenanceAndSafetyDefinitionsAreNotRewritten()
    {
        const string source="11111111-1111-1111-1111-111111111111 isolate and verify zero energy; page 3";
        Assert.Equal(source,HostedDataBoundary.Redact(source));
    }
    [Fact]
    public async Task ActualConfiguredKeyCannotLeaveInsidePromptAndOutputIsBounded()
    {
        var handler=Fixtures.Handler(Fixtures.Completion);
        using var provider=new OpenAiLlmProvider(Fixtures.Options(),handler);
        await provider.CompleteAsync(new([new(LlmRole.User,"test-key-not-a-credential")]),default);
        Assert.DoesNotContain("test-key-not-a-credential",handler.Body!.ToJsonString());
        await Assert.ThrowsAsync<ArgumentException>(()=>provider.CompleteAsync(new(Fixtures.Query.Messages,maxTokens:16385),default));
        await Assert.ThrowsAsync<ArgumentException>(()=>provider.CompleteAsync(new([new(LlmRole.User,new string('a',1048577))]),default));
        Assert.Equal(1,handler.Sends);
    }
    [Fact]
    public async Task UnboundedNonStreamingProviderBodyIsRejected()
    {
        using var provider=new OpenAiLlmProvider(Fixtures.Options(),Fixtures.Handler(new string(' ',4194305)));
        var failure=await Assert.ThrowsAsync<LlmProviderException>(()=>provider.CompleteAsync(Fixtures.Query,default));
        Assert.Equal(LlmProviderFailureKind.InvalidResponse,failure.Kind);
    }

    [Fact]
    public async Task HostedEmbeddingInputsAreRedactedButLocalProviderKeepsLocalText()
    {
        var handler=Fixtures.Handler(Fixtures.Completion);
        using var hosted=new OpenAiLlmProvider(Fixtures.Options(),handler);
        await Assert.ThrowsAsync<LlmProviderException>(()=>hosted.GenerateEmbeddingsAsync(new(["person@example.test"]),default));
        Assert.Equal("[REDACTED]",handler.Body!["input"]![0]!.GetValue<string>());
        var localHandler=Fixtures.Handler(Fixtures.Completion);
        using var local=new OllamaLlmProvider(OllamaProviderTests.Options,localHandler);
        await local.CompleteAsync(new([new(LlmRole.User,"person@example.test")]),default);
        Assert.Equal("person@example.test",localHandler.Body!["messages"]![0]!["content"]!.GetValue<string>());
    }
    [Fact]
    public void ToolArgumentRedactionPreservesJsonAndCorrelationIdentity()
    {
        var payload=System.Text.Json.Nodes.JsonNode.Parse("""{"messages":[{"content":"", "tool_calls":[{"id":"call-1","function":{"name":"retrieve_evidence","arguments":"{\"password\":\"example-value\",\"DocumentId\":\"11111111-1111-1111-1111-111111111111\"}"}}]}]}""")!.AsObject();
        HostedDataBoundary.Apply(payload,null);
        var call=payload["messages"]![0]!["tool_calls"]![0]!;
        Assert.Equal("call-1",call["id"]!.GetValue<string>());
        using var args=System.Text.Json.JsonDocument.Parse(call["function"]!["arguments"]!.GetValue<string>());
        Assert.Equal("[REDACTED]",args.RootElement.GetProperty("password").GetString());
        Assert.Equal("11111111-1111-1111-1111-111111111111",args.RootElement.GetProperty("DocumentId").GetString());
    }
}
