using System.Net;
using IndustrialCopilot.Application.Abstractions.AI;
using IndustrialCopilot.Application.Abstractions.AI.Models;
using IndustrialCopilot.Infrastructure.AI;

namespace IndustrialCopilot.Infrastructure.Tests.AI;

public class OpenAiBoundaryTests
{
    [Theory]
    [InlineData("http://api.openai.com/")]
    [InlineData("https://untrusted.example/")]
    [InlineData("https://api.openai.com/v1/")]
    [InlineData("https://api.openai.com/?key=secret")]
    [InlineData("https://user:secret@api.openai.com/")]
    [InlineData("https://api.openai.com/#fragment")]
    [InlineData("https://api.openai.com:444/")]
    public void RejectsUnsafeOrAmbiguousHostedEndpoints(string endpoint) =>
        Assert.Throws<ArgumentException>(() => new OpenAiOptions(new Uri(endpoint), "chat", "embedding", "test-key", TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1)));

    [Fact]
    public void ValidatesRequiredConfigurationWithoutExposingSecret()
    {
        Assert.Throws<ArgumentException>(() => new OpenAiOptions(new("https://api.openai.com/"), " ", "embed", "test-key", TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1)));
        Assert.Throws<ArgumentException>(() => new OpenAiOptions(new("https://api.openai.com/"), "chat", " ", "test-key", TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1)));
        Assert.Throws<ArgumentException>(() => new OpenAiOptions(new("https://api.openai.com/"), "chat", "embed", " ", TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1)));
        Assert.Throws<ArgumentException>(() => new OpenAiOptions(new("https://api.openai.com/"), "chat", "embed", "secret\r\nheader", TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => Fixtures.Options(requestTimeout: TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => Fixtures.Options(streamTimeout: Timeout.InfiniteTimeSpan));
        Assert.DoesNotContain("test-key", Fixtures.Options().ToString());
    }

    [Theory]
    [InlineData(401, LlmProviderFailureKind.Authentication)]
    [InlineData(403, LlmProviderFailureKind.Authentication)]
    [InlineData(429, LlmProviderFailureKind.RateLimited)]
    [InlineData(400, LlmProviderFailureKind.InvalidRequest)]
    [InlineData(503, LlmProviderFailureKind.Unavailable)]
    [InlineData(408, LlmProviderFailureKind.Timeout)]
    [InlineData(307, LlmProviderFailureKind.HttpFailure)]
    public async Task HttpErrorsAreSanitizedAndNeverRetried(int status, LlmProviderFailureKind kind)
    {
        var handler = new FakeHandler(_ => Task.FromResult(Fixtures.Json("secret prompt credentials", (HttpStatusCode)status)));
        using var provider = new OpenAiLlmProvider(Fixtures.Options(), handler);
        var error = await Assert.ThrowsAsync<LlmProviderException>(() => provider.CompleteAsync(Fixtures.Query, default));
        Assert.Equal(kind, error.Kind);
        Assert.Null(error.InnerException);
        Assert.DoesNotContain("secret", error.ToString());
        Assert.Equal(1, handler.Sends);
    }

    [Fact]
    public async Task RawTransportFailureDoesNotEscape()
    {
        using var provider = new OpenAiLlmProvider(Fixtures.Options(), new FakeHandler(_ => throw new HttpRequestException("secret upstream details")));
        var error = await Assert.ThrowsAsync<LlmProviderException>(() => provider.CompleteAsync(Fixtures.Query, default));
        Assert.Equal(LlmProviderFailureKind.Transport, error.Kind);
        Assert.Null(error.InnerException);
        Assert.DoesNotContain("secret", error.ToString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CallerCancellationStopsSendAndBodyReads(bool duringBody)
    {
        using var cancellation = new CancellationTokenSource();
        var waiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stream = new FragmentedStream("{", blockAtEnd: true);
        var handler = new FakeHandler(async token =>
        {
            if (duringBody) return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(stream) };
            waiting.SetResult();
            await Task.Delay(Timeout.Infinite, token);
            throw new InvalidOperationException("Unreachable");
        });
        using var provider = new OpenAiLlmProvider(Fixtures.Options(), handler);
        var pending = provider.CompleteAsync(Fixtures.Query, cancellation.Token);
        await (duringBody ? stream.Waiting.Task : waiting.Task).WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Equal(cancellation.Token, error.CancellationToken);
        if (duringBody) Assert.True(stream.WasDisposed);
    }

    [Fact]
    public async Task AlreadyCancelledCallerDoesNotSend()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var handler = Fixtures.Handler(Fixtures.Completion);
        using var provider = new OpenAiLlmProvider(Fixtures.Options(), handler);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.CompleteAsync(Fixtures.Query, cancellation.Token));
        Assert.Equal(0, handler.Sends);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RequestDeadlineCoversSendAndBody(bool duringBody)
    {
        var stream = new FragmentedStream("{", blockAtEnd: true);
        var handler = new FakeHandler(async token =>
        {
            if (duringBody) return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(stream) };
            await Task.Delay(Timeout.Infinite, token);
            throw new InvalidOperationException("Unreachable");
        });
        using var provider = new OpenAiLlmProvider(Fixtures.Options(requestTimeout: TimeSpan.FromMilliseconds(150)), handler);
        var error = await Assert.ThrowsAsync<LlmProviderException>(() => provider.CompleteAsync(Fixtures.Query, default));
        Assert.Equal(LlmProviderFailureKind.Timeout, error.Kind);
    }

    [Fact]
    public async Task CancellationTakesPrecedenceOverRacingTransportFailure()
    {
        using var cancellation = new CancellationTokenSource();
        using var provider = new OpenAiLlmProvider(Fixtures.Options(), new FakeHandler(_ =>
        {
            cancellation.Cancel();
            throw new HttpRequestException("private failure details");
        }));
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.CompleteAsync(Fixtures.Query, cancellation.Token));
        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.Null(error.InnerException);
    }

    [Theory]
    [InlineData("{\"private-prompt\": broken}")]
    [InlineData("{\"error\":{\"message\":\"private-prompt\"}}")]
    public async Task MalformedSuccessBodyCannotLeakProviderPayload(string json)
    {
        using var provider = new OpenAiLlmProvider(Fixtures.Options(), Fixtures.Handler(json));
        var error = await Assert.ThrowsAsync<LlmProviderException>(() => provider.CompleteAsync(Fixtures.Query, default));
        Assert.Equal(LlmProviderFailureKind.InvalidResponse, error.Kind);
        Assert.DoesNotContain("private-prompt", error.ToString());
        Assert.Null(error.InnerException);
    }

    [Fact]
    public void DomainAndApplicationAssembliesKeepTheirDependencyBoundaries()
    {
        var applicationReferences = typeof(ILlmProvider).Assembly.GetReferencedAssemblies().Select(a => a.Name!).ToArray();
        Assert.All(applicationReferences, name => Assert.True(name.StartsWith("System.") || name == "IndustrialCopilot.Domain", name));
        var domain = System.Reflection.Assembly.Load("IndustrialCopilot.Domain");
        Assert.All(domain.GetReferencedAssemblies(), reference => Assert.StartsWith("System.", reference.Name!));
    }
}
