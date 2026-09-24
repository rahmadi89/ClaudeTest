using System.Security.Claims;
using System.Text.Encodings.Web;
using AtmMonitor.Contracts;
using AtmMonitor.Infrastructure.Persistence;
using AtmMonitor.Infrastructure.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AtmMonitor.Api.Auth;

/// <summary>
/// Authenticates ATM agents by (agent id, agent key) headers. The key is issued once at enrollment and stored
/// server-side only as a SHA-256 hash. Deploy behind TLS; for defence-in-depth also enable mTLS at the edge
/// (see docs/SECURITY.md).
/// </summary>
public sealed class AgentKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder, AppDbContext db)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "AgentKey";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(Protocol.AgentIdHeader, out var idValue) ||
            !Request.Headers.TryGetValue(Protocol.AgentKeyHeader, out var keyValue))
        {
            return AuthenticateResult.NoResult();
        }

        if (!Guid.TryParse(idValue, out var atmId) || string.IsNullOrEmpty(keyValue))
        {
            return AuthenticateResult.Fail("Malformed agent credentials.");
        }

        var atm = await db.Atms.AsNoTracking()
            .Where(a => a.Id == atmId)
            .Select(a => new { a.Id, a.TerminalId, a.AgentKeyHash, a.IsEnabled })
            .FirstOrDefaultAsync(Context.RequestAborted);

        if (atm is null || !atm.IsEnabled || !SecretHasher.Verify(keyValue.ToString(), atm.AgentKeyHash))
        {
            Logger.LogWarning("Agent authentication failed for {AtmId} from {Ip}", atmId, Context.Connection.RemoteIpAddress);
            return AuthenticateResult.Fail("Invalid agent credentials.");
        }

        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, atm.Id.ToString()),
            new Claim(ClaimTypes.Name, $"agent:{atm.TerminalId}"),
            new Claim(ClaimTypes.Role, Policies.AgentRole),
            new Claim(Policies.TerminalIdClaim, atm.TerminalId),
        ], SchemeName);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
    }
}
