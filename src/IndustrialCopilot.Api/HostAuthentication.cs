using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using IndustrialCopilot.Infrastructure.Operations;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace IndustrialCopilot.Api;

public sealed record HostCredential(string Actor,string Secret,IReadOnlySet<string> Permissions,IReadOnlySet<Guid> Equipment)
{ public override string ToString() => "HostCredential [redacted]"; }
public sealed class HostAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options,ILoggerFactory logger,UrlEncoder encoder,IReadOnlyList<HostCredential> credentials)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options,logger,encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header=Request.Headers.Authorization.ToString();
        if(!header.StartsWith("Bearer ",StringComparison.Ordinal)) return Task.FromResult(AuthenticateResult.NoResult());
        var secret=header[7..];
        if(secret.Length>512) return Task.FromResult(AuthenticateResult.Fail("Invalid credential."));
        var digest=SHA256.HashData(Encoding.UTF8.GetBytes(secret));
        var credential=credentials.FirstOrDefault(c=>CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(c.Secret)),digest));
        if(credential is null) return Task.FromResult(AuthenticateResult.Fail("Invalid credential."));
        var claims=new List<Claim>{new(ClaimTypes.NameIdentifier,credential.Actor)};
        claims.AddRange(credential.Permissions.Select(p=>new Claim("permission",p)));
        claims.AddRange(credential.Equipment.Select(e=>new Claim("equipment",e.ToString("D"))));
        return Task.FromResult(AuthenticateResult.Success(new(new ClaimsPrincipal(new ClaimsIdentity(claims,Scheme.Name)),Scheme.Name)));
    }
    public static HostIdentity? Identity(HttpContext? context)
    {
        if(context?.User.Identity?.IsAuthenticated!=true) return null;
        return new(context.User.FindFirstValue(ClaimTypes.NameIdentifier)!,context.User.FindAll("permission").Select(c=>c.Value).ToHashSet(),context.User.FindAll("equipment").Select(c=>Guid.Parse(c.Value)).ToHashSet());
    }
    public static IReadOnlyList<HostCredential> Read(IConfiguration config)
    {
        var allowed=new HashSet<string>{"read","start","approve","verify","dispatch"}; var result=new List<HostCredential>();
        foreach(var entry in config.GetSection("Authentication:Credentials").GetChildren())
        {
            var actor=entry["Actor"]; var secret=entry["Secret"]; var permissions=(entry["Permissions"]??"").Split(',',StringSplitOptions.RemoveEmptyEntries).ToHashSet();
            var equipment=(entry["EquipmentIds"]??"").Split(',',StringSplitOptions.RemoveEmptyEntries).Select(s=>Guid.TryParse(s,out var id)?id:Guid.Empty).ToHashSet();
            if(string.IsNullOrWhiteSpace(actor)||actor.Length>100 || string.IsNullOrWhiteSpace(secret)||secret.Length<32||secret.Length>512 || permissions.Count==0 || !permissions.IsSubsetOf(allowed) || equipment.Count==0 || equipment.Contains(Guid.Empty)) throw new InvalidOperationException("Invalid host credential configuration.");
            result.Add(new(actor,secret,permissions,equipment));
        }
        if(result.Count==0 || result.Select(c=>c.Actor).Distinct().Count()!=result.Count || result.Select(c=>c.Secret).Distinct().Count()!=result.Count) throw new InvalidOperationException("Unique authenticated host credentials are required.");
        return result.AsReadOnly();
    }
}
