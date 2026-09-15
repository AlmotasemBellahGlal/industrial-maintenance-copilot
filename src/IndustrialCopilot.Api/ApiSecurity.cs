using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace IndustrialCopilot.Api;

/// <summary>Single-instance admission control. Identity is established before partition selection.</summary>
internal static class ApiSecurity
{
    internal static void Register(WebApplicationBuilder builder)
    {
        int Read(string key, int fallback, int min, int max)
        {
            var value = builder.Configuration["Security:" + key] is {} text ? int.Parse(text, CultureInfo.InvariantCulture) : fallback;
            if (value < min || value > max) throw new InvalidOperationException("Invalid security limit configuration.");
            return value;
        }
        var permits = Read("MutationPermits", 30, 1, 300);
        var seconds = Read("WindowSeconds", 60, 1, 3600);
        builder.Services.AddRateLimiter(options =>
        {
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            {
                if (!context.Request.Path.StartsWithSegments("/api") || !HttpMethods.IsPost(context.Request.Method))
                    return RateLimitPartition.GetNoLimiter("unmetered");
                // Resource IDs are not partition keys: rotating IDs must not evade the actor budget.
                var actor = context.User.Identity?.IsAuthenticated == true ? context.User.FindFirstValue(ClaimTypes.NameIdentifier)! : "anonymous";
                return RateLimitPartition.GetFixedWindowLimiter(actor, _ => new FixedWindowRateLimiterOptions
                { PermitLimit = permits, Window = TimeSpan.FromSeconds(seconds), QueueLimit = 0, AutoReplenishment = true });
            });
            options.OnRejected = async (rejection, token) =>
            {
                var context = rejection.HttpContext;
                context.Response.StatusCode = 429;
                context.Response.Headers.RetryAfter = Math.Max(1, (int)Math.Ceiling(rejection.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retry) ? retry.TotalSeconds : seconds)).ToString(CultureInfo.InvariantCulture);
                await context.Response.WriteAsJsonAsync(ApiMessages.Failure(context, 429, "rate_limited"), token);
            };
        });
        var origins = builder.Configuration.GetSection("Security:AllowedOrigins").Get<string[]>() ?? [];
        if (origins.Any(origin => !Uri.TryCreate(origin, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https") || uri.UserInfo.Length != 0 || uri.AbsolutePath != "/" || uri.Query.Length != 0 || uri.Fragment.Length != 0 || origin.Contains('*')))
            throw new InvalidOperationException("Explicit HTTP origins required.");
        builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
        {
            if (origins.Length > 0) policy.WithOrigins(origins).WithMethods("GET", "POST").WithHeaders("Authorization", "Content-Type", "Accept-Language", "X-Correlation-ID", "Idempotency-Key", "Last-Event-ID")
                .WithExposedHeaders("X-Correlation-ID", "Retry-After", "Location", "Content-Language");
        }));
        builder.Services.AddHsts(options => { options.MaxAge = TimeSpan.FromDays(180); });
    }
}
