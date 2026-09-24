using AtmMonitor.Domain.Entities;
using AtmMonitor.Infrastructure.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AtmMonitor.Infrastructure.Persistence;

public static class DbInitializer
{
    /// <summary>
    /// Applies migrations (PostgreSQL) or creates the schema (SQLite, dev/test only) and bootstraps the first admin.
    /// </summary>
    public static async Task InitializeAsync(
        AppDbContext db, IPasswordHasher<User> hasher, TimeProvider clock, ILogger logger,
        string? bootstrapAdminPassword, string? bootstrapEnrollmentToken = null, CancellationToken ct = default)
    {
        if (db.Database.IsSqlite())
        {
            await db.Database.EnsureCreatedAsync(ct);
        }
        else
        {
            await db.Database.MigrateAsync(ct);
        }

        await SeedEnrollmentTokenAsync(db, clock, logger, bootstrapEnrollmentToken, ct);

        if (await db.Users.AnyAsync(ct))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(bootstrapAdminPassword))
        {
            logger.LogWarning("No users exist and Bootstrap:AdminPassword is not set; nobody can log in until an admin is created.");
            return;
        }

        var admin = new User
        {
            UserName = "admin", DisplayName = "Administrator", Role = UserRole.Admin, CreatedAt = clock.GetUtcNow(),
        };
        admin.PasswordHash = hasher.HashPassword(admin, bootstrapAdminPassword);
        db.Users.Add(admin);
        await db.SaveChangesAsync(ct);
        logger.LogWarning("Bootstrapped initial 'admin' user. Change its password and remove Bootstrap:AdminPassword from configuration.");
    }

    /// <summary>
    /// Lets automated environments (docker-compose demo, CI, lab) enroll agents without a UI step.
    /// Never set Bootstrap:EnrollmentToken in production – create short-lived tokens in the console instead.
    /// </summary>
    private static async Task SeedEnrollmentTokenAsync(AppDbContext db, TimeProvider clock, ILogger logger, string? token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return;
        }

        var hash = SecretHasher.Hash(token);
        if (await db.EnrollmentTokens.AnyAsync(t => t.TokenHash == hash, ct))
        {
            return;
        }

        var now = clock.GetUtcNow();
        db.EnrollmentTokens.Add(new EnrollmentToken
        {
            TokenHash = hash, Description = "Bootstrap token from configuration", CreatedAt = now, CreatedBy = "system",
            ExpiresAt = now.AddDays(7), MaxUses = 100,
        });
        await db.SaveChangesAsync(ct);
        logger.LogWarning("Seeded bootstrap enrollment token from configuration (7 days, 100 uses). Do not use this in production.");
    }
}
