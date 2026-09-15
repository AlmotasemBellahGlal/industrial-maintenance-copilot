using Microsoft.Extensions.Localization;

namespace IndustrialCopilot.Api;

/// <summary>Safe presentation text only. Error codes and policy decisions are never localized.</summary>
public sealed class ApiMessages
{
    internal static object Failure(HttpContext context, int status, string code)
    {
        var messages = context.RequestServices.GetRequiredService<IStringLocalizer<ApiMessages>>();
        var key = status switch
        {
            400 => "InvalidRequest", 401 => "Unauthenticated", 403 => "Forbidden",
            413 => "PayloadTooLarge", 429 => "RateLimited", 404 => "NotFound", 409 => "Conflict", 422 => "NotAccepted",
            504 => "Timeout", _ => "Unavailable"
        };
        return new { error = code, title = messages[key].Value, detail = messages[key + "Detail"].Value,
            correlationId = context.Items["correlation"] };
    }
}
