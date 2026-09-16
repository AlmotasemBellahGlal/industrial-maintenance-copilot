using System.Text.Json;
using IndustrialCopilot.Application.Abstractions.AI.Models;

namespace IndustrialCopilot.Application.Tests.Abstractions.AI;

public class LlmContractTests
{
    private static JsonElement ObjectJson() => JsonSerializer.SerializeToElement(new { equipment = "P-101" });
    private static ToolCall Call(string id = "call-1") => new(id, "inspect", ObjectJson());
    private static TokenUsage Usage() => new(1, 2, 3);

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void RejectsNonpositiveMaxTokens(int maxTokens) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new CompletionRequest([], maxTokens: maxTokens));

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void RejectsNonfiniteTemperature(double temperature) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new CompletionRequest([], temperature));

    [Fact]
    public void OptionalLimitsAndFiniteTemperaturesAreNotProviderRestricted()
    {
        _ = new CompletionRequest([]);
        _ = new CompletionRequest([], -3.0, 1);
        _ = new CompletionRequest([], 3.0);
    }

    [Theory]
    [InlineData(-1, 0, 0)]
    [InlineData(0, -1, 0)]
    [InlineData(0, 0, -1)]
    public void RejectsNegativeUsage(int prompt, int completion, int total) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new TokenUsage(prompt, completion, total));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void RejectsMissingToolNamesAndCallIds(string? value)
    {
        Assert.ThrowsAny<ArgumentException>(() => new ToolDefinition(value!, "Inspect", ObjectJson()));
        Assert.ThrowsAny<ArgumentException>(() => new ToolCall(value!, "inspect", ObjectJson()));
        Assert.ThrowsAny<ArgumentException>(() => new ToolCall("call-1", value!, ObjectJson()));
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("1")]
    [InlineData("true")]
    [InlineData("\"text\"")]
    public void RejectsNonobjectJson(string json)
    {
        using var document = JsonDocument.Parse(json);
        Assert.Throws<ArgumentException>(() => new ToolDefinition("inspect", "Inspect", document.RootElement));
        Assert.Throws<ArgumentException>(() => new ToolCall("call-1", "inspect", document.RootElement));
    }

    [Fact]
    public void RejectsUndefinedJson()
    {
        Assert.Throws<ArgumentException>(() => new ToolDefinition("inspect", "Inspect", default));
        Assert.Throws<ArgumentException>(() => new ToolCall("call-1", "inspect", default));
    }

    [Fact]
    public void JsonSurvivesDisposalOfSourceDocument()
    {
        ToolDefinition definition;
        ToolCall call;
        using (var document = JsonDocument.Parse("{\"equipment\":\"P-101\"}"))
        {
            definition = new ToolDefinition("inspect", "Inspect", document.RootElement);
            call = new ToolCall("call-1", "inspect", document.RootElement);
        }
        Assert.Equal("P-101", definition.ArgumentsSchema.GetProperty("equipment").GetString());
        Assert.Equal("P-101", call.Arguments.GetProperty("equipment").GetString());
    }

    [Fact]
    public void RepresentsMultipleToolCallsResultsAndFinalAssistantResponse()
    {
        var response = new ToolCompletionResponse("", [Call(), Call("call-2")], Usage());
        var messages = new List<LlmMessage>
        {
            new(LlmRole.User, "Inspect equipment"),
            new(LlmRole.Assistant, response.Content, response.ToolCalls)
        };
        foreach (var call in response.ToolCalls)
            messages.Add(new LlmMessage(LlmRole.Tool, "Inspection result", toolCallId: call.Id, toolName: call.Name));
        messages.Add(new LlmMessage(LlmRole.Assistant, "Inspection complete"));
        var request = new CompletionRequest(messages);
        Assert.Equal(2, request.Messages[1].ToolCalls.Count);
        Assert.Equal(request.Messages[1].ToolCalls[0].Id, request.Messages[2].ToolCallId);
        Assert.Equal(request.Messages[1].ToolCalls[1].Id, request.Messages[3].ToolCallId);
        Assert.Equal("inspect", request.Messages[2].ToolName);
        Assert.Empty(request.Messages[4].ToolCalls);
        Assert.Empty(new ToolCompletionResponse("Final answer", [], Usage()).ToolCalls);
    }

    [Fact]
    public void RejectsInvalidMessageRoleAndCorrelationCombinations()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LlmMessage((LlmRole)0, ""));
        Assert.Throws<ArgumentException>(() => new LlmMessage(LlmRole.User, "", [Call()]));
        Assert.ThrowsAny<ArgumentException>(() => new LlmMessage(LlmRole.Tool, "result"));
        Assert.ThrowsAny<ArgumentException>(() => new LlmMessage(LlmRole.Tool, "result", toolCallId: " ", toolName: "inspect"));
        Assert.ThrowsAny<ArgumentException>(() => new LlmMessage(LlmRole.Tool, "result", toolCallId: "call-1", toolName: " "));
        Assert.Throws<ArgumentException>(() => new LlmMessage(LlmRole.Assistant, "", toolCallId: "call-1"));
        Assert.Throws<ArgumentException>(() => new LlmMessage(LlmRole.User, "", toolName: "inspect"));
    }

    [Fact]
    public void RequiredCollectionsAndElementsCannotBeNull()
    {
        Assert.Throws<ArgumentNullException>(() => new CompletionRequest(null!));
        Assert.Throws<ArgumentException>(() => new CompletionRequest([null!]));
        Assert.Throws<ArgumentNullException>(() => new EmbeddingRequest(null!));
        Assert.Throws<ArgumentException>(() => new EmbeddingRequest([null!]));
        Assert.Throws<ArgumentNullException>(() => new EmbeddingResult(null!));
        Assert.Throws<ArgumentException>(() => new EmbeddingResult([null!]));
        Assert.Throws<ArgumentNullException>(() => new ToolCompletionResponse("", null!, Usage()));
        Assert.Throws<ArgumentException>(() => new ToolCompletionResponse("", [null!], Usage()));
        Assert.Throws<ArgumentException>(() => new LlmMessage(LlmRole.Assistant, "", [null!]));
    }

    [Fact]
    public void RequestsAndToolCollectionsOwnTheirSnapshots()
    {
        var calls = new List<ToolCall> { Call() };
        var assistant = new LlmMessage(LlmRole.Assistant, "", calls);
        var response = new ToolCompletionResponse("", calls, Usage());
        var messages = new List<LlmMessage> { assistant };
        var request = new CompletionRequest(messages);
        var inputs = new[] { "original" };
        var embedding = new EmbeddingRequest(inputs);
        calls.Clear();
        messages.Clear();
        inputs[0] = "changed";
        Assert.Single(request.Messages);
        Assert.Single(assistant.ToolCalls);
        Assert.Single(response.ToolCalls);
        Assert.Equal("original", embedding.Inputs[0]);
        Assert.Throws<NotSupportedException>(() => ((IList<LlmMessage>)request.Messages).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<ToolCall>)assistant.ToolCalls).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<ToolCall>)response.ToolCalls).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<string>)embedding.Inputs)[0] = "changed");
    }

    [Fact]
    public void EmbeddingVectorsAreProtectedAtBothCollectionLevels()
    {
        var first = new float[] { 1, 2 };
        var second = new List<float> { 3, 4 };
        var vectors = new List<IReadOnlyList<float>> { first, second };
        var result = new EmbeddingResult(vectors);
        first[0] = 99;
        second.Clear();
        vectors.Clear();
        Assert.Equal(new float[] { 1, 2 }, result.Vectors[0]);
        Assert.Equal(new float[] { 3, 4 }, result.Vectors[1]);
        Assert.Throws<NotSupportedException>(() => ((IList<IReadOnlyList<float>>)result.Vectors).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<float>)result.Vectors[0])[0] = 99);
    }

    [Fact]
    public void EmptyResponseContentIsValidButNullRequiredValuesAreNot()
    {
        _ = new CompletionResponse("", Usage());
        _ = new ToolCompletionResponse("", [Call()], Usage());
        _ = new StreamingChunk("", true, Usage());
        Assert.Throws<ArgumentNullException>(() => new CompletionResponse(null!, Usage()));
        Assert.Null(new CompletionResponse("", null).Usage);
        Assert.Throws<ArgumentNullException>(() => new ToolCompletionResponse(null!, [], Usage()));
        Assert.Null(new ToolCompletionResponse("", [], null).Usage);
        Assert.Throws<ArgumentNullException>(() => new LlmMessage(LlmRole.User, null!));
        Assert.Throws<ArgumentNullException>(() => new ToolDefinition("inspect", null!, ObjectJson()));
        Assert.Throws<ArgumentNullException>(() => new StreamingChunk(null!, false));
        Assert.Throws<ArgumentException>(() => new StreamingChunk("", false, Usage()));
    }
}
