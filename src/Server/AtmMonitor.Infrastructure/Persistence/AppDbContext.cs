using System.Text.Json;
using AtmMonitor.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace AtmMonitor.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Atm> Atms => Set<Atm>();
    public DbSet<AtmComponent> Components => Set<AtmComponent>();
    public DbSet<Cassette> Cassettes => Set<Cassette>();
    public DbSet<AtmCommand> Commands => Set<AtmCommand>();
    public DbSet<Alert> Alerts => Set<Alert>();
    public DbSet<User> Users => Set<User>();
    public DbSet<EnrollmentToken> EnrollmentTokens => Set<EnrollmentToken>();
    public DbSet<TelemetrySample> Telemetry => Set<TelemetrySample>();
    public DbSet<AtmLogEntry> Logs => Set<AtmLogEntry>();
    public DbSet<AuditEntry> Audit => Set<AuditEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var b = modelBuilder;
        b.Entity<Atm>(e =>
        {
            e.HasIndex(x => x.TerminalId).IsUnique();
            e.HasIndex(x => x.Status);
            e.Property(x => x.TerminalId).HasMaxLength(32);
            e.Property(x => x.Name).HasMaxLength(128);
            e.Property(x => x.Branch).HasMaxLength(128);
            e.Property(x => x.Address).HasMaxLength(256);
            e.Property(x => x.City).HasMaxLength(128);
            e.Property(x => x.Vendor).HasMaxLength(64);
            e.Property(x => x.Model).HasMaxLength(64);
            e.Property(x => x.SerialNumber).HasMaxLength(64);
            e.Property(x => x.AgentKeyHash).HasMaxLength(128);
            e.Property(x => x.AgentVersion).HasMaxLength(32);
            e.Property(x => x.MachineName).HasMaxLength(128);
            e.Property(x => x.OsDescription).HasMaxLength(256);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(24);
            e.Property(x => x.Mode).HasConversion<string>().HasMaxLength(24);
            e.OwnsOne(x => x.Network, n =>
            {
                n.Property(p => p.LocalIpAddress).HasMaxLength(64);
                n.Property(p => p.InterfaceName).HasMaxLength(128);
            });
            e.OwnsOne(x => x.System, s => s.Property(p => p.OsDescription).HasMaxLength(256));
            e.HasMany(x => x.Components).WithOne().HasForeignKey(x => x.AtmId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Cassettes).WithOne().HasForeignKey(x => x.AtmId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<AtmComponent>(e =>
        {
            e.HasIndex(x => new { x.AtmId, x.Type }).IsUnique();
            e.Property(x => x.Type).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.State).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.ErrorCode).HasMaxLength(64);
            e.Property(x => x.Description).HasMaxLength(512);
        });

        b.Entity<Cassette>(e =>
        {
            e.HasIndex(x => new { x.AtmId, x.CassetteId }).IsUnique();
            e.Property(x => x.CassetteId).HasMaxLength(16);
            e.Property(x => x.Currency).HasMaxLength(3);
            e.Property(x => x.Denomination).HasPrecision(18, 2);
            e.Property(x => x.Type).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
            e.Ignore(x => x.FillPercent);
        });

        var dictComparer = new ValueComparer<Dictionary<string, string>>(
            (a, c) => a!.Count == c!.Count && !a.Except(c).Any(),
            v => v.Aggregate(0, (h, kv) => HashCode.Combine(h, kv.Key.GetHashCode(StringComparison.Ordinal), kv.Value.GetHashCode(StringComparison.Ordinal))),
            v => new Dictionary<string, string>(v));

        b.Entity<AtmCommand>(e =>
        {
            e.HasIndex(x => new { x.AtmId, x.Status });
            e.HasIndex(x => x.CreatedAt);
            e.HasOne(x => x.Atm).WithMany().HasForeignKey(x => x.AtmId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.Type).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.RequestedBy).HasMaxLength(128);
            e.Property(x => x.Reason).HasMaxLength(512);
            e.Property(x => x.Version).IsConcurrencyToken();
            e.Property(x => x.Parameters)
                .HasConversion(
                    v => JsonSerializer.Serialize(v, JsonSerializerOptions.Default),
                    v => JsonSerializer.Deserialize<Dictionary<string, string>>(v, JsonSerializerOptions.Default) ?? new())
                .Metadata.SetValueComparer(dictComparer);
        });

        b.Entity<Alert>(e =>
        {
            e.HasIndex(x => new { x.AtmId, x.DedupKey, x.Status });
            e.HasIndex(x => new { x.Status, x.RaisedAt });
            e.HasOne(x => x.Atm).WithMany().HasForeignKey(x => x.AtmId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.Type).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.Severity).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.DedupKey).HasMaxLength(128);
            e.Property(x => x.Message).HasMaxLength(1024);
            e.Property(x => x.AcknowledgedBy).HasMaxLength(128);
            e.Property(x => x.ResolvedBy).HasMaxLength(128);
            e.Property(x => x.Note).HasMaxLength(1024);
        });

        b.Entity<User>(e =>
        {
            e.HasIndex(x => x.UserName).IsUnique();
            e.Property(x => x.UserName).HasMaxLength(64);
            e.Property(x => x.DisplayName).HasMaxLength(128);
            e.Property(x => x.PasswordHash).HasMaxLength(512);
            e.Property(x => x.Role).HasConversion<string>().HasMaxLength(16);
        });

        b.Entity<EnrollmentToken>(e =>
        {
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.Property(x => x.TokenHash).HasMaxLength(128);
            e.Property(x => x.Description).HasMaxLength(256);
            e.Property(x => x.CreatedBy).HasMaxLength(128);
        });

        b.Entity<TelemetrySample>(e =>
        {
            e.HasIndex(x => new { x.AtmId, x.Timestamp });
            e.Property(x => x.AvailableCash).HasPrecision(18, 2);
        });

        b.Entity<AtmLogEntry>(e =>
        {
            e.HasIndex(x => new { x.AtmId, x.Timestamp });
            e.HasIndex(x => x.ReceivedAt);
            e.Property(x => x.Severity).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.Source).HasMaxLength(128);
            e.Property(x => x.Message).HasMaxLength(8192);
        });

        b.Entity<AuditEntry>(e =>
        {
            e.HasIndex(x => x.Timestamp);
            e.Property(x => x.Actor).HasMaxLength(128);
            e.Property(x => x.Action).HasMaxLength(64);
            e.Property(x => x.TargetType).HasMaxLength(64);
            e.Property(x => x.TargetId).HasMaxLength(64);
            e.Property(x => x.Details).HasMaxLength(2048);
            e.Property(x => x.IpAddress).HasMaxLength(64);
        });

        if (Database.IsSqlite())
        {
            ApplySqliteConventions(b);
        }
    }

    /// <summary>
    /// SQLite cannot order/compare DateTimeOffset or aggregate decimal natively. Store them as sortable
    /// integers / REAL so the same LINQ works in dev and tests as on PostgreSQL.
    /// </summary>
    private static void ApplySqliteConventions(ModelBuilder b)
    {
        foreach (var entity in b.Model.GetEntityTypes())
        {
            foreach (var p in entity.GetProperties())
            {
                if (p.ClrType == typeof(DateTimeOffset) || p.ClrType == typeof(DateTimeOffset?))
                {
                    p.SetValueConverter(new DateTimeOffsetToBinaryConverter());
                }
                else if (p.ClrType == typeof(decimal) || p.ClrType == typeof(decimal?))
                {
                    p.SetValueConverter(new ValueConverter<decimal, double>(v => (double)v, v => (decimal)v));
                }
            }
        }
    }
}
