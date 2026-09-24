namespace AtmMonitor.Contracts;

/// <summary>
/// Full state snapshot pushed by the agent on a fixed interval and whenever something changes.
/// The server treats every report as the latest truth for the terminal (last-writer-wins by <see cref="CapturedAt"/>).
/// </summary>
public sealed record StatusReport
{
    public required DateTimeOffset CapturedAt { get; init; }
    public required string AgentVersion { get; init; }
    public OperationalMode Mode { get; init; } = OperationalMode.Unknown;
    public IReadOnlyList<ComponentStatusDto> Components { get; init; } = [];
    public IReadOnlyList<CassetteDto> Cassettes { get; init; } = [];
    public NetworkStatusDto? Network { get; init; }
    public SystemMetricsDto? System { get; init; }
}

public sealed record ComponentStatusDto(
    ComponentType Type,
    ComponentState State,
    string? ErrorCode = null,
    string? Description = null);

public sealed record CassetteDto(
    string CassetteId,
    CassetteType Type,
    string Currency,
    decimal Denomination,
    int Count,
    int Capacity,
    CassetteStatus Status);

public sealed record NetworkStatusDto(
    bool HostReachable,
    double? HostLatencyMs,
    double PacketLossPercent,
    string? LocalIpAddress,
    string? InterfaceName,
    bool InterfaceUp,
    long? LinkSpeedMbps);

public sealed record SystemMetricsDto(
    double CpuPercent,
    double MemoryUsedPercent,
    double DiskUsedPercent,
    long DiskFreeBytes,
    TimeSpan Uptime,
    string OsDescription,
    string MachineName);

public sealed record LogEntryDto(
    DateTimeOffset Timestamp,
    LogSeverity Severity,
    string Source,
    string Message);

public sealed record LogBatch(IReadOnlyList<LogEntryDto> Entries);

/// <summary>Command pushed from server to agent.</summary>
public sealed record CommandEnvelope(
    Guid CommandId,
    CommandType Type,
    IReadOnlyDictionary<string, string> Parameters,
    DateTimeOffset IssuedAt,
    DateTimeOffset ExpiresAt);

/// <summary>Progress / result update for a command, sent from agent to server. Idempotent per (CommandId, Status).</summary>
public sealed record CommandUpdate(
    Guid CommandId,
    CommandStatus Status,
    string? Output = null,
    string? Error = null,
    DateTimeOffset? Timestamp = null);

public sealed record EnrollmentRequest(
    string EnrollmentToken,
    string TerminalId,
    string MachineName,
    string AgentVersion,
    string OsDescription);

public sealed record EnrollmentResponse(Guid AtmId, string AgentKey);
