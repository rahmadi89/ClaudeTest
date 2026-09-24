using System.ComponentModel.DataAnnotations;
using AtmMonitor.Contracts;
using AtmMonitor.Domain.Entities;

namespace AtmMonitor.Api.Models;

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);

public sealed record PageQuery
{
    [Range(1, int.MaxValue)] public int Page { get; init; } = 1;
    [Range(1, 500)] public int PageSize { get; init; } = 50;
    public int Skip => (Page - 1) * PageSize;
}

// ---- Auth ----
public sealed record LoginRequest([Required, MaxLength(64)] string UserName, [Required, MaxLength(256)] string Password);
public sealed record UserInfo(Guid Id, string UserName, string DisplayName, UserRole Role);
public sealed record LoginResponse(string AccessToken, DateTimeOffset ExpiresAt, UserInfo User);
public sealed record ChangePasswordRequest([Required] string CurrentPassword, [Required, MinLength(12), MaxLength(256)] string NewPassword);

// ---- ATMs ----
public sealed record AtmListItem(
    Guid Id, string TerminalId, string Name, string? Branch, string? City, AtmStatus Status, OperationalMode Mode,
    bool IsEnabled, bool IsEnrolled, bool IsConnected, DateTimeOffset? LastSeenAt, string? AgentVersion,
    int OpenAlerts, decimal AvailableCash, int LowCassettes);

public sealed record AtmDetail(
    Guid Id, string TerminalId, string Name, string? Branch, string? Address, string? City,
    double? Latitude, double? Longitude, string? Vendor, string? Model, string? SerialNumber,
    bool IsEnabled, bool IsEnrolled, bool IsConnected, DateTimeOffset CreatedAt, DateTimeOffset? EnrolledAt,
    AtmStatus Status, OperationalMode Mode, DateTimeOffset? LastSeenAt, DateTimeOffset? LastReportAt, DateTimeOffset? StatusChangedAt,
    string? AgentVersion, string? MachineName, string? OsDescription,
    IReadOnlyList<ComponentView> Components, IReadOnlyList<CassetteView> Cassettes,
    NetworkSnapshot? Network, SystemSnapshot? System, IReadOnlyDictionary<string, decimal> AvailableCash);

public sealed record ComponentView(ComponentType Type, ComponentState State, string? ErrorCode, string? Description, DateTimeOffset UpdatedAt, DateTimeOffset StateChangedAt);

public sealed record CassetteView(string CassetteId, CassetteType Type, string Currency, decimal Denomination, int Count, int Capacity, CassetteStatus Status, double FillPercent, decimal Value);

public sealed record UpsertAtmRequest
{
    [Required, RegularExpression("^[A-Za-z0-9_-]{1,32}$")] public string TerminalId { get; init; } = "";
    [Required, MaxLength(128)] public string Name { get; init; } = "";
    [MaxLength(128)] public string? Branch { get; init; }
    [MaxLength(256)] public string? Address { get; init; }
    [MaxLength(128)] public string? City { get; init; }
    [Range(-90, 90)] public double? Latitude { get; init; }
    [Range(-180, 180)] public double? Longitude { get; init; }
    [MaxLength(64)] public string? Vendor { get; init; }
    [MaxLength(64)] public string? Model { get; init; }
    [MaxLength(64)] public string? SerialNumber { get; init; }
}

public sealed record TelemetryPoint(DateTimeOffset Timestamp, double? CpuPercent, double? MemoryUsedPercent, double? DiskUsedPercent, double? HostLatencyMs, double? PacketLossPercent, bool? HostReachable, decimal? AvailableCash);

public sealed record LogView(long Id, DateTimeOffset Timestamp, LogSeverity Severity, string Source, string Message);

// ---- Commands ----
public sealed record CreateCommandRequest
{
    [Required] public CommandType Type { get; init; }
    public Dictionary<string, string>? Parameters { get; init; }
    [MaxLength(512)] public string? Reason { get; init; }
}

public sealed record CommandView(
    Guid Id, Guid AtmId, string? TerminalId, CommandType Type, IReadOnlyDictionary<string, string> Parameters, CommandStatus Status,
    string RequestedBy, string? Reason, DateTimeOffset CreatedAt, DateTimeOffset? SentAt, DateTimeOffset? CompletedAt,
    DateTimeOffset ExpiresAt, string? Output, string? Error);

// ---- Alerts ----
public sealed record AlertView(
    Guid Id, Guid AtmId, string? TerminalId, string? AtmName, AlertType Type, AlertSeverity Severity, AlertStatus Status,
    string Message, DateTimeOffset RaisedAt, DateTimeOffset LastOccurredAt, int OccurrenceCount,
    DateTimeOffset? AcknowledgedAt, string? AcknowledgedBy, DateTimeOffset? ResolvedAt, string? ResolvedBy, string? Note);

public sealed record AlertActionRequest([MaxLength(1024)] string? Note);

// ---- Dashboard ----
public sealed record DashboardSummary(
    int TotalAtms, IReadOnlyDictionary<AtmStatus, int> ByStatus, IReadOnlyDictionary<AlertSeverity, int> OpenAlertsBySeverity,
    IReadOnlyDictionary<string, decimal> CashByCurrency, int AtmsWithLowCash, int PendingCommands,
    IReadOnlyList<AlertView> RecentAlerts);

// ---- Admin ----
public sealed record CreateEnrollmentTokenRequest(
    [Required, MaxLength(256)] string Description,
    [Range(1, 10000)] int MaxUses = 1,
    [Range(1, 24 * 30)] int ValidHours = 72);

public sealed record EnrollmentTokenView(Guid Id, string Description, DateTimeOffset CreatedAt, string CreatedBy, DateTimeOffset ExpiresAt, int MaxUses, int UseCount, bool IsRevoked);

public sealed record CreatedEnrollmentToken(EnrollmentTokenView Token, string Secret);

public sealed record UserView(Guid Id, string UserName, string DisplayName, UserRole Role, bool IsActive, DateTimeOffset CreatedAt, DateTimeOffset? LastLoginAt, bool IsLockedOut);

public sealed record CreateUserRequest(
    [Required, RegularExpression("^[a-zA-Z0-9._-]{3,64}$")] string UserName,
    [Required, MaxLength(128)] string DisplayName,
    [Required] UserRole Role,
    [Required, MinLength(12), MaxLength(256)] string Password);

public sealed record UpdateUserRequest([Required, MaxLength(128)] string DisplayName, [Required] UserRole Role, bool IsActive);

public sealed record ResetPasswordRequest([Required, MinLength(12), MaxLength(256)] string NewPassword);

public sealed record AuditView(long Id, DateTimeOffset Timestamp, string Actor, string Action, string? TargetType, string? TargetId, string? Details, string? IpAddress);
