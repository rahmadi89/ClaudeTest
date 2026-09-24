using AtmMonitor.Api.Auth;
using AtmMonitor.Api.Models;
using AtmMonitor.Api.Services;
using AtmMonitor.Domain.Entities;
using AtmMonitor.Infrastructure.Persistence;
using AtmMonitor.Infrastructure.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AtmMonitor.Api.Controllers;

[ApiController]
[Route("api/admin")]
[Authorize(Policy = Policies.Admin)]
public sealed class AdminController(AppDbContext db, IPasswordHasher<User> hasher, AuditLogger audit, TimeProvider clock) : ControllerBase
{
    // ---- Enrollment tokens ----

    [HttpGet("enrollment-tokens")]
    public async Task<IReadOnlyList<EnrollmentTokenView>> ListTokens(CancellationToken ct) =>
        (await db.EnrollmentTokens.AsNoTracking().OrderByDescending(t => t.CreatedAt).Take(200).ToListAsync(ct))
        .Select(t => t.ToView()).ToList();

    /// <summary>Creates a token. The secret is returned exactly once and never stored in plain text.</summary>
    [HttpPost("enrollment-tokens")]
    public async Task<CreatedEnrollmentToken> CreateToken(CreateEnrollmentTokenRequest request, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var secret = SecretHasher.GenerateSecret();
        var token = new EnrollmentToken
        {
            TokenHash = SecretHasher.Hash(secret), Description = request.Description, CreatedAt = now,
            CreatedBy = User.UserName(), ExpiresAt = now.AddHours(request.ValidHours), MaxUses = request.MaxUses,
        };
        db.EnrollmentTokens.Add(token);
        audit.Record("enrollment-token.create", "EnrollmentToken", token.Id.ToString(), $"{request.Description}, uses {request.MaxUses}");
        await db.SaveChangesAsync(ct);
        return new CreatedEnrollmentToken(token.ToView(), secret);
    }

    [HttpDelete("enrollment-tokens/{id:guid}")]
    public async Task<IActionResult> RevokeToken(Guid id, CancellationToken ct)
    {
        var token = await db.EnrollmentTokens.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (token is null)
        {
            return NotFound();
        }

        token.RevokedAt ??= clock.GetUtcNow();
        audit.Record("enrollment-token.revoke", "EnrollmentToken", id.ToString());
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ---- Users ----

    [HttpGet("users")]
    public async Task<IReadOnlyList<UserView>> ListUsers(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        return (await db.Users.AsNoTracking().OrderBy(u => u.UserName).ToListAsync(ct)).Select(u => u.ToView(now)).ToList();
    }

    [HttpPost("users")]
    public async Task<ActionResult<UserView>> CreateUser(CreateUserRequest request, CancellationToken ct)
    {
        if (await db.Users.AnyAsync(u => u.UserName == request.UserName, ct))
        {
            return Conflict(new ProblemDetails { Title = "User name already exists.", Status = 409 });
        }

        var now = clock.GetUtcNow();
        var user = new User { UserName = request.UserName, DisplayName = request.DisplayName, Role = request.Role, CreatedAt = now };
        user.PasswordHash = hasher.HashPassword(user, request.Password);
        db.Users.Add(user);
        audit.Record("user.create", "User", user.UserName, $"role {user.Role}");
        await db.SaveChangesAsync(ct);
        return user.ToView(now);
    }

    [HttpPut("users/{id:guid}")]
    public async Task<ActionResult<UserView>> UpdateUser(Guid id, UpdateUserRequest request, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null)
        {
            return NotFound();
        }

        if (user.Id == User.UserId() && (request.Role != UserRole.Admin || !request.IsActive))
        {
            return ValidationProblem("You cannot demote or deactivate your own account.");
        }

        audit.Record("user.update", "User", user.UserName, $"role {user.Role}->{request.Role}, active {user.IsActive}->{request.IsActive}");
        user.DisplayName = request.DisplayName;
        user.Role = request.Role;
        user.IsActive = request.IsActive;
        await db.SaveChangesAsync(ct);
        return user.ToView(clock.GetUtcNow());
    }

    [HttpPost("users/{id:guid}/reset-password")]
    public async Task<IActionResult> ResetPassword(Guid id, ResetPasswordRequest request, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null)
        {
            return NotFound();
        }

        user.PasswordHash = hasher.HashPassword(user, request.NewPassword);
        user.FailedLoginCount = 0;
        user.LockoutEndsAt = null;
        audit.Record("user.password.reset", "User", user.UserName);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ---- Audit ----

    [HttpGet("audit")]
    public async Task<PagedResult<AuditView>> Audit([FromQuery] PageQuery page, [FromQuery] string? actor, [FromQuery] string? action, CancellationToken ct)
    {
        var q = db.Audit.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(actor))
        {
            q = q.Where(a => a.Actor == actor);
        }

        if (!string.IsNullOrWhiteSpace(action))
        {
            q = q.Where(a => a.Action.StartsWith(action));
        }

        var total = await q.CountAsync(ct);
        var items = await q.OrderByDescending(a => a.Id).Skip(page.Skip).Take(page.PageSize)
            .Select(a => new AuditView(a.Id, a.Timestamp, a.Actor, a.Action, a.TargetType, a.TargetId, a.Details, a.IpAddress))
            .ToListAsync(ct);
        return new PagedResult<AuditView>(items, total, page.Page, page.PageSize);
    }
}
