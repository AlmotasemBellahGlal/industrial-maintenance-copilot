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
/// <summary>Fixed rights of anonymous demo visitors. Present only when public sessions are enabled.</summary>
public sealed record DemoSessionPolicy(IReadOnlySet<string> Permissions,IReadOnlySet<Guid> Equipment);
public sealed class HostAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options,ILoggerFactory logger,UrlEncoder encoder,IReadOnlyList<HostCredential> credentials,IDemoSessions? sessions=null,DemoSessionPolicy? policy=null)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options,logger,encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header=Request.Headers.Authorization.ToString();
        if(!header.StartsWith("Bearer ",StringComparison.Ordinal)) return AuthenticateResult.NoResult();
        var secret=header[7..];
        if(secret.Length>512) return AuthenticateResult.Fail("Invalid credential.");
        var digest=SHA256.HashData(Encoding.UTF8.GetBytes(secret));
        var credential=credentials.FirstOrDefault(c=>CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(c.Secret)),digest));
        string actor; IReadOnlySet<string> permissions; IReadOnlySet<Guid> equipment;
        if(credential is not null) (actor,permissions,equipment)=(credential.Actor,credential.Permissions,credential.Equipment);
        else if(sessions is not null && policy is not null && PostgresDemoSessions.LooksLikeSecret(secret) && await sessions.AuthenticateAsync(secret,Context.RequestAborted) is {} visitor)
            (actor,permissions,equipment)=(visitor,policy.Permissions,policy.Equipment);
        else return AuthenticateResult.Fail("Invalid credential.");
        var claims=new List<Claim>{new(ClaimTypes.NameIdentifier,actor)};
        claims.AddRange(permissions.Select(p=>new Claim("permission",p)));
        claims.AddRange(equipment.Select(e=>new Claim("equipment",e.ToString("D"))));
        return AuthenticateResult.Success(new(new ClaimsPrincipal(new ClaimsIdentity(claims,Scheme.Name)),Scheme.Name));
    }
    public static HostIdentity? Identity(HttpContext? context)
    {
        if(context?.User.Identity?.IsAuthenticated!=true) return null;
        return new(context.User.FindFirstValue(ClaimTypes.NameIdentifier)!,context.User.FindAll("permission").Select(c=>c.Value).ToHashSet(),context.User.FindAll("equipment").Select(c=>Guid.Parse(c.Value)).ToHashSet());
    }
    public static IReadOnlyList<HostCredential> Read(IConfiguration config)
    {
        var allowed=new HashSet<string>{"read","start","approve","verify","dispatch","ingest"}; var result=new List<HostCredential>();
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
