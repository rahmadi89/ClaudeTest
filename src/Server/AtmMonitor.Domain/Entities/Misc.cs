using AtmMonitor.Contracts;

namespace AtmMonitor.Domain.Entities;

public enum UserRole
{
    Viewer = 0,
    Operator = 1,
    Admin = 2,
}

public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string UserName { get; set; }
    public required string DisplayName { get; set; }
    public string PasswordHash { get; set; } = "";
    public UserRole Role { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }
    public int FailedLoginCount { get; set; }
    public DateTimeOffset? LockoutEndsAt { get; set; }
}

public class EnrollmentToken
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string TokenHash { get; set; }
    public required string Description { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public required string CreatedBy { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public int MaxUses { get; set; } = 1;
    public int UseCount { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }

    public bool IsUsable(DateTimeOffset now) => RevokedAt is null && now < ExpiresAt && UseCount < MaxUses;
}

/// <summary>Time-series point, one per status report. Kept small; retention is enforced by a background job.</summary>
public class TelemetrySample
{
    public long Id { get; set; }
    public Guid AtmId { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public double? CpuPercent { get; set; }
    public double? MemoryUsedPercent { get; set; }
    public double? DiskUsedPercent { get; set; }
    public double? HostLatencyMs { get; set; }
    public double? PacketLossPercent { get; set; }
    public bool? HostReachable { get; set; }
    public decimal? AvailableCash { get; set; }
}

public class AtmLogEntry
{
    public long Id { get; set; }
    public Guid AtmId { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
    public LogSeverity Severity { get; set; }
    public required string Source { get; set; }
    public required string Message { get; set; }
}

public class AuditEntry
{
    public long Id { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public required string Actor { get; set; }
    public required string Action { get; set; }
    public string? TargetType { get; set; }
    public string? TargetId { get; set; }
    public string? Details { get; set; }
    public string? IpAddress { get; set; }
}
