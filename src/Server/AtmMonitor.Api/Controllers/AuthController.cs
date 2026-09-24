using AtmMonitor.Api.Auth;
using AtmMonitor.Api.Models;
using AtmMonitor.Api.Services;
using AtmMonitor.Domain.Entities;
using AtmMonitor.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace AtmMonitor.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(
    AppDbContext db, IPasswordHasher<User> hasher, JwtTokenService tokens, AuditLogger audit, TimeProvider clock) : ControllerBase
{
    private const int MaxFailedAttempts = 5;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimits.Login)]
    public async Task<ActionResult<LoginResponse>> Login(LoginRequest request, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var user = await db.Users.FirstOrDefaultAsync(u => u.UserName == request.UserName, ct);

        // Same response for unknown user, bad password, disabled and locked accounts: no user enumeration.
        if (user is null || !user.IsActive || user.LockoutEndsAt > now)
        {
            audit.Record("auth.login.failed", "User", request.UserName, actor: request.UserName);
            await db.SaveChangesAsync(ct);
            return Unauthorized(Problem401());
        }

        var result = hasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
        if (result == PasswordVerificationResult.Failed)
        {
            user.FailedLoginCount++;
            if (user.FailedLoginCount >= MaxFailedAttempts)
            {
                user.LockoutEndsAt = now + LockoutDuration;
                user.FailedLoginCount = 0;
                audit.Record("auth.lockout", "User", user.UserName, actor: user.UserName);
            }

            audit.Record("auth.login.failed", "User", user.UserName, actor: user.UserName);
            await db.SaveChangesAsync(ct);
            return Unauthorized(Problem401());
        }

        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = hasher.HashPassword(user, request.Password);
        }

        user.FailedLoginCount = 0;
        user.LockoutEndsAt = null;
        user.LastLoginAt = now;
        audit.Record("auth.login", "User", user.UserName, actor: user.UserName);
        await db.SaveChangesAsync(ct);

        var (token, expires) = tokens.Issue(user);
        return new LoginResponse(token, expires, user.ToInfo());
    }

    [HttpGet("me")]
    public async Task<ActionResult<UserInfo>> Me(CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == User.UserId() && u.IsActive, ct);
        return user is null ? Unauthorized() : user.ToInfo();
    }

    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == User.UserId(), ct);
        if (user is null || hasher.VerifyHashedPassword(user, user.PasswordHash, request.CurrentPassword) == PasswordVerificationResult.Failed)
        {
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                ["currentPassword"] = ["Current password is incorrect."],
            }));
        }

        user.PasswordHash = hasher.HashPassword(user, request.NewPassword);
        audit.Record("auth.password.changed", "User", user.UserName);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private static ProblemDetails Problem401() => new() { Title = "Invalid username or password.", Status = 401 };
}
