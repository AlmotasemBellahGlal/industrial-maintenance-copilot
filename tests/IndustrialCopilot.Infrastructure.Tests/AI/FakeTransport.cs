using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using IndustrialCopilot.Application.Abstractions.AI.Models;
using IndustrialCopilot.Infrastructure.AI;

namespace IndustrialCopilot.Infrastructure.Tests.AI;

internal sealed class FakeHandler(Func<CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
{
    internal JsonObject? Body { get; private set; }
    internal Uri? Uri { get; private set; }
    internal string? Authorization { get; private set; }
    internal int Sends { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Sends++;
        Uri = request.RequestUri;
        Authorization = request.Headers.Authorization?.ToString();
        Body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!.AsObject();
        return await respond(cancellationToken);
    }
}

internal sealed class FragmentedStream(string text, int fragmentSize = 1, bool blockAtEnd = false) : Stream
{
    private readonly byte[] bytes = Encoding.UTF8.GetBytes(text);
    private int position;
    internal TaskCompletionSource Waiting { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal bool WasDisposed { get; private set; }
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (position == bytes.Length && blockAtEnd)
        {
            Waiting.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
        var count = Math.Min(Math.Min(fragmentSize, buffer.Length), bytes.Length - position);
        bytes.AsMemory(position, count).CopyTo(buffer);
        position += count;
        return count;
    }
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override void Flush() => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    protected override void Dispose(bool disposing) { WasDisposed = true; base.Dispose(disposing); }
}

internal static class Fixtures
{
    internal static OpenAiOptions Options(TimeSpan? requestTimeout = null, TimeSpan? streamTimeout = null) =>
        new(new Uri("https://api.openai.com/"), "test-chat-model", "test-embedding-model", "test-key-not-a-credential",
            requestTimeout ?? TimeSpan.FromSeconds(10), streamTimeout ?? TimeSpan.FromSeconds(10));
    internal static CompletionRequest Query => new([new LlmMessage(LlmRole.User, "Inspect pump")]);
    internal const string Completion = """
        {"model":"actual-model","choices":[{"index":0,"finish_reason":"stop","message":{"role":"assistant","content":"Check isolation."}}],"usage":{"prompt_tokens":3,"completion_tokens":2,"total_tokens":5}}
        """;
    internal static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) => new(status)
        { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    internal static FakeHandler Handler(string json) => new(_ => Task.FromResult(Json(json)));
    internal static HttpResponseMessage StreamResponse(Stream stream) => new(HttpStatusCode.OK)
    {
        Content = new StreamContent(stream) { Headers = { ContentType = new("text/event-stream") } }
    };
    internal static string Delta(string content) => "data: " + new JsonObject
    {
        ["choices"] = new JsonArray(new JsonObject { ["index"] = 0, ["delta"] = new JsonObject { ["content"] = content }, ["finish_reason"] = null })
    }.ToJsonString() + "\n\n";
    internal const string Finish = "data: {\"choices\":[{\"index\":0,\"delta\":{},\"finish_reason\":\"stop\"}]}\n\n";
    internal const string Usage = "data: {\"choices\":[],\"usage\":{\"prompt_tokens\":3,\"completion_tokens\":2,\"total_tokens\":5}}\n\n";
    internal const string Done = "data: [DONE]\n\n";
}
