using System.Net;
using System.Net.Sockets;
using IndustrialCopilot.Infrastructure.AI;

namespace IndustrialCopilot.Infrastructure.Tests.AI;

public class ProviderFailureTests
{
    [Theory]
    [InlineData(429, "{\"error\":{\"code\":\"rate_limit_exceeded\",\"type\":\"tokens\"}}", LlmProviderFailureKind.TemporaryRateLimit, true)]
    [InlineData(429, "{\"error\":{\"code\":\"insufficient_quota\"}}", LlmProviderFailureKind.QuotaExceeded, false)]
    [InlineData(429, "{\"error\":{\"code\":\"rate_limit_exceeded\",\"type\":\"insufficient_quota\"}}", LlmProviderFailureKind.QuotaExceeded, false)]
    [InlineData(429, "{\"error\":{\"code\":\"billing_hard_limit_reached\"}}", LlmProviderFailureKind.QuotaExceeded, false)]
    [InlineData(429, "not-json", LlmProviderFailureKind.RateLimited, false)]
    [InlineData(429, "{\"error\":\"busy\"}", LlmProviderFailureKind.RateLimited, false)]
    [InlineData(503, "{\"error\":{\"type\":\"invalid_request_error\"}}", LlmProviderFailureKind.InvalidRequest, false)]
    [InlineData(500, "{\"error\":{\"code\":\"model_not_found\"}}", LlmProviderFailureKind.InvalidRequest, false)]
    [InlineData(503, "", LlmProviderFailureKind.Unavailable, true)]
    [InlineData(502, "bad gateway", LlmProviderFailureKind.Unavailable, true)]
    [InlineData(408, "private body", LlmProviderFailureKind.Timeout, true)]
    public async Task StatusAndSafeErrorCodesDetermineEligibility(int status, string body, LlmProviderFailureKind expected, bool eligible)
    {
        using var provider = new OpenAiLlmProvider(Fixtures.Options(), new FakeHandler(_ => Task.FromResult(Fixtures.Json(body, (HttpStatusCode)status))));
        var error = await Assert.ThrowsAsync<LlmProviderException>(() => provider.CompleteAsync(Fixtures.Query, default));
        Assert.Equal(expected, error.Kind);
        Assert.Equal(eligible, ProviderFailures.CanFallback(error));
        Assert.Null(error.InnerException);
        Assert.DoesNotContain(body.Length == 0 ? "bad gateway" : body, error.ToString());
    }

    [Theory]
    [InlineData(SocketError.ConnectionRefused, true)]
    [InlineData(SocketError.ConnectionReset, true)]
    [InlineData(SocketError.TryAgain, true)]
    [InlineData(SocketError.HostNotFound, false)]
    [InlineData(SocketError.AccessDenied, false)]
    public async Task OnlyKnownTransientSocketErrorsAreEligible(SocketError socketError, bool eligible)
    {
        using var provider = new OpenAiLlmProvider(Fixtures.Options(), new FakeHandler(_ => throw new HttpRequestException("private connection detail", new SocketException((int)socketError))));
        var error = await Assert.ThrowsAsync<LlmProviderException>(() => provider.CompleteAsync(Fixtures.Query, default));
        Assert.Equal(eligible, ProviderFailures.CanFallback(error));
        Assert.DoesNotContain("private", error.ToString());
    }

    [Fact]
    public async Task TlsErrorIsNotTransientEvenWithSocketCause()
    {
        using var provider = new OpenAiLlmProvider(Fixtures.Options(), new FakeHandler(_ => throw new HttpRequestException(
            HttpRequestError.SecureConnectionError, "private TLS detail", new SocketException((int)SocketError.ConnectionReset))));
        var error = await Assert.ThrowsAsync<LlmProviderException>(() => provider.CompleteAsync(Fixtures.Query, default));
        Assert.False(ProviderFailures.CanFallback(error));
    }

    [Fact]
    public async Task UnknownRateLimitBodyTimeoutDoesNotBecomeEligibleTimeout()
    {
        var stream = new FragmentedStream("{", blockAtEnd: true);
        using var provider = new OpenAiLlmProvider(Fixtures.Options(requestTimeout: TimeSpan.FromMilliseconds(150)),
            new FakeHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests) { Content = new StreamContent(stream) })));
        var error = await Assert.ThrowsAsync<LlmProviderException>(() => provider.CompleteAsync(Fixtures.Query, default));
        Assert.Equal(LlmProviderFailureKind.RateLimited, error.Kind);
        Assert.False(ProviderFailures.CanFallback(error));
    }

    [Fact]
    public async Task CancellationWhileClassifyingErrorRemainsCallerCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        var stream = new FragmentedStream("{", blockAtEnd: true);
        using var provider = new OpenAiLlmProvider(Fixtures.Options(),
            new FakeHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests) { Content = new StreamContent(stream) })));
        var pending = provider.CompleteAsync(Fixtures.Query, cancellation.Token);
        await stream.Waiting.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
    }
}
