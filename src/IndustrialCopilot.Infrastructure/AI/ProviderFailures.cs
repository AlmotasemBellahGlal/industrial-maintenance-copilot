using System.Net.Sockets;
using System.Text.Json;

namespace IndustrialCopilot.Infrastructure.AI;

internal static class ProviderFailures
{
    internal static bool CanFallback(LlmProviderException error) => error.Kind is
        LlmProviderFailureKind.TransientTransport or LlmProviderFailureKind.Timeout or
        LlmProviderFailureKind.TemporaryRateLimit or LlmProviderFailureKind.Unavailable;

    internal static LlmProviderException Transport(Exception error)
    {
        // Do not treat TLS, proxy authentication, permanent DNS errors, or an unknown
        // IOException as transient simply because they occurred in the transport.
        if (error is HttpRequestException http && http.HttpRequestError is not
            (HttpRequestError.Unknown or HttpRequestError.ConnectionError or HttpRequestError.NameResolutionError))
            return new(LlmProviderFailureKind.Transport);
        for (Exception? cause = error; cause is not null; cause = cause.InnerException)
            if (cause is SocketException socket && socket.SocketErrorCode is
                SocketError.ConnectionReset or SocketError.ConnectionAborted or SocketError.ConnectionRefused or
                SocketError.TimedOut or SocketError.TryAgain or SocketError.NetworkDown or
                SocketError.NetworkUnreachable or SocketError.HostUnreachable)
                return new(LlmProviderFailureKind.TransientTransport);
        return new(LlmProviderFailureKind.Transport);
    }

    internal static async Task CheckStatusAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var status = (int)response.StatusCode;
        if (status is >= 200 and <= 299) return;
        var kind = status switch
        {
            401 or 403 => LlmProviderFailureKind.Authentication,
            429 => LlmProviderFailureKind.RateLimited, // Unknown 429 is NOT fallback eligible.
            408 => LlmProviderFailureKind.Timeout,
            400 or 404 or 422 => LlmProviderFailureKind.InvalidRequest,
            500 or 502 or 503 or 504 => LlmProviderFailureKind.Unavailable,
            _ => LlmProviderFailureKind.HttpFailure
        };
        // Only a bounded body is inspected, never stored in the exception or logged.
        // Inspect 5xx too: an explicit model/configuration error must override status.
        if (status == 429 || status is 500 or 502 or 503 or 504)
        {
            try
            {
                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                var buffer = new byte[16 * 1024 + 1];
                var length = 0;
                while (length < buffer.Length)
                {
                    var read = await stream.ReadAsync(buffer.AsMemory(length), cancellationToken).ConfigureAwait(false);
                    if (read == 0) break;
                    length += read;
                }
                if (length == buffer.Length) kind = status == 429 ? LlmProviderFailureKind.RateLimited : LlmProviderFailureKind.HttpFailure;
                else
                {
                    using var json = JsonDocument.Parse(buffer.AsMemory(0, length));
                    if (json.RootElement.ValueKind == JsonValueKind.Object &&
                        json.RootElement.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object)
                    {
                        var code = Text(error, "code");
                        var type = Text(error, "type");
                        if (IsQuota(code) || IsQuota(type)) kind = LlmProviderFailureKind.QuotaExceeded;
                        else if (code is "model_not_found" or "invalid_api_key" or "invalid_request_error" || type == "invalid_request_error")
                            kind = LlmProviderFailureKind.InvalidRequest;
                        else if (status == 429 && code == "rate_limit_exceeded") kind = LlmProviderFailureKind.TemporaryRateLimit;
                    }
                }
            }
            catch (JsonException)
            {
                // Status alone still identifies a service error; an unknown 429 stays unknown.
            }
            catch (Exception error) when (error is IOException or HttpRequestException or OperationCanceledException)
            {
                // An unreadable/malformed error body is never evidence of a transient failure.
                kind = status == 429 ? LlmProviderFailureKind.RateLimited : LlmProviderFailureKind.HttpFailure;
            }
        }
        throw new LlmProviderException(kind);
    }

    private static string? Text(JsonElement value, string name) =>
        value.TryGetProperty(name, out var text) && text.ValueKind == JsonValueKind.String ? text.GetString() : null;

    private static bool IsQuota(string? code) => code is "insufficient_quota" or "billing_hard_limit_reached" or "billing_not_active";
}
