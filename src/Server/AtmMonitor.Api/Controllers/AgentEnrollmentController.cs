using AtmMonitor.Api.Hubs;
using AtmMonitor.Api.Services;
using AtmMonitor.Contracts;
using AtmMonitor.Domain.Entities;
using AtmMonitor.Infrastructure.Persistence;
using AtmMonitor.Infrastructure.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace AtmMonitor.Api.Controllers;

/// <summary>
/// Exchanges a one-time (or limited-use) enrollment token for a long-lived per-terminal agent key.
/// If the terminal was pre-registered by an admin it is bound; otherwise it is created.
/// Re-enrolling an existing terminal rotates its key and drops any live connection using the old key.
/// </summary>
[ApiController]
[AllowAnonymous]
public sealed class AgentEnrollmentController(
    AppDbContext db, AuditLogger audit, AgentConnectionRegistry agents, TimeProvider clock, ILogger<AgentEnrollmentController> logger) : ControllerBase
{
    [HttpPost(Protocol.EnrollPath)]
    [EnableRateLimiting(RateLimits.Enroll)]
    public async Task<ActionResult<EnrollmentResponse>> Enroll(EnrollmentRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.TerminalId) || request.TerminalId.Length > 32 ||
            !request.TerminalId.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_'))
        {
            return ValidationProblem("Invalid terminal id.");
        }

        var now = clock.GetUtcNow();
        var tokenHash = SecretHasher.Hash(request.EnrollmentToken ?? "");
        var token = await db.EnrollmentTokens.FirstOrDefaultAsync(t => t.TokenHash == tokenHash, ct);
        if (token is null || !token.IsUsable(now))
        {
            logger.LogWarning("Rejected enrollment for {TerminalId} from {Ip}: invalid token", request.TerminalId, HttpContext.Connection.RemoteIpAddress);
            audit.Record("agent.enroll.rejected", "Atm", request.TerminalId, actor: $"agent:{request.TerminalId}");
            await db.SaveChangesAsync(ct);
            return Unauthorized(new ProblemDetails { Title = "Invalid or expired enrollment token.", Status = 401 });
        }

        var atm = await db.Atms.FirstOrDefaultAsync(a => a.TerminalId == request.TerminalId, ct);
        if (atm is { IsEnabled: false })
        {
            return StatusCode(403, new ProblemDetails { Title = "Terminal is disabled.", Status = 403 });
        }

        var isNew = atm is null;
        atm ??= new Atm { TerminalId = request.TerminalId, Name = request.TerminalId, CreatedAt = now };
        if (isNew)
        {
            db.Atms.Add(atm);
        }

        var key = SecretHasher.GenerateSecret();
        atm.AgentKeyHash = SecretHasher.Hash(key);
        atm.EnrolledAt = now;
        atm.MachineName = Clip(request.MachineName, 128);
        atm.AgentVersion = Clip(request.AgentVersion, 32);
        atm.OsDescription = Clip(request.OsDescription, 256);
        token.UseCount++;

        audit.Record(isNew ? "agent.enroll.new" : "agent.enroll.rekey", "Atm", atm.TerminalId,
            $"token {token.Id}; machine {request.MachineName}", actor: $"agent:{request.TerminalId}");
        await db.SaveChangesAsync(ct);

        if (!isNew)
        {
            agents.Disconnect(atm.Id);
        }

        logger.LogInformation("Enrolled terminal {TerminalId} ({AtmId}), new={IsNew}", atm.TerminalId, atm.Id, isNew);
        return new EnrollmentResponse(atm.Id, key);
    }

    private static string? Clip(string? s, int max) => s is null ? null : s.Length <= max ? s : s[..max];
}
