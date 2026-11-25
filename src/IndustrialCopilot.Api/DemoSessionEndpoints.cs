using System.Net;
using IndustrialCopilot.Infrastructure.Operations;

namespace IndustrialCopilot.Api;

/// <summary>Anonymous temporary sessions for the public synthetic demo. Mapped only when Demo:PublicSessions=true.</summary>
public static class DemoSessionEndpoints
{
    public const string Route="/api/demo/session";
    public static void Map(WebApplication app,DemoSessionOptions options)
    {
        app.MapGet(Route,()=>Results.Ok(new{enabled=true,lifetimeSeconds=(int)options.SessionLifetime.TotalSeconds})).AllowAnonymous();
        app.MapPost(Route,async(HttpContext c)=>
        {
            // Body is ignored and never read. The client key is the connection address, which is the
            // forwarded client address only when it came from the configured trusted proxy network.
            var key=PostgresDemoSessions.ClientKey(c.Connection.RemoteIpAddress?.ToString());
            var (outcome,session)=await c.RequestServices.GetRequiredService<IDemoSessions>().MintAsync(key,c.RequestAborted);
            switch(outcome)
            {
                case DemoMintOutcome.ClientLimit: c.Response.Headers.RetryAfter="3600"; throw new ApiProblemException(429,"session_limit");
                case DemoMintOutcome.CapacityFull: c.Response.Headers.RetryAfter="300"; throw new ApiProblemException(503,"demo_full");
            }
            return Results.Json(new{token=session!.Secret,expiresAt=session.ExpiresAt},statusCode:201);
        }).AllowAnonymous();
    }
    /// <summary>Trust X-Forwarded-* only from the configured proxy network; otherwise the headers are ignored.</summary>
    public static void ConfigureForwarding(WebApplicationBuilder builder)
    {
        var cidr=builder.Configuration["Demo:TrustedProxyCidr"];
        if(string.IsNullOrWhiteSpace(cidr)) return;
        var parts=cidr.Split('/');
        if(parts.Length!=2||!IPAddress.TryParse(parts[0],out var prefix)||!int.TryParse(parts[1],out var bits)||bits<8||bits>32)
            throw new InvalidOperationException("Demo:TrustedProxyCidr must be an IPv4 network such as 172.29.0.0/24 (prefix 8 to 32).");
        builder.Services.Configure<Microsoft.AspNetCore.Builder.ForwardedHeadersOptions>(o=>
        {
            o.ForwardedHeaders=Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor;
            o.ForwardLimit=1; o.KnownProxies.Clear(); o.KnownNetworks.Clear();
            o.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(prefix,bits));
        });
    }
}
