namespace AtmMonitor.Contracts;

/// <summary>Well-known routes and header names shared by server and agent.</summary>
public static class Protocol
{
    public const string AgentHubPath = "/hubs/agent";
    public const string DashboardHubPath = "/hubs/dashboard";
    public const string EnrollPath = "/api/agent/enroll";

    public const string AgentIdHeader = "X-Agent-Id";
    public const string AgentKeyHeader = "X-Agent-Key";

    /// <summary>Bumped on any breaking change to the agent/server message shapes.</summary>
    public const int Version = 1;
    public const string VersionHeader = "X-Agent-Protocol";
}

/// <summary>Methods the server may invoke on a connected agent.</summary>
public interface IAgentClient
{
    Task ExecuteCommand(CommandEnvelope command);
}

/// <summary>Hub method names the agent invokes on the server (kept as constants so both sides share one source of truth).</summary>
public static class AgentHubMethods
{
    public const string ReportStatus = nameof(ReportStatus);
    public const string ReportLogs = nameof(ReportLogs);
    public const string ReportCommandUpdate = nameof(ReportCommandUpdate);
}

/// <summary>Real-time events broadcast to dashboard (browser) clients.</summary>
public interface IDashboardClient
{
    Task AtmUpdated(AtmSummaryEvent atm);
    Task AlertChanged(AlertEvent alert);
    Task CommandChanged(CommandEvent command);
}

public sealed record AtmSummaryEvent(Guid AtmId, string TerminalId, AtmStatus Status, OperationalMode Mode, DateTimeOffset? LastSeenAt);

public sealed record AlertEvent(Guid AlertId, Guid AtmId, string TerminalId, AlertType Type, AlertSeverity Severity, AlertStatus Status, string Message, DateTimeOffset RaisedAt);

public sealed record CommandEvent(Guid CommandId, Guid AtmId, CommandType Type, CommandStatus Status, DateTimeOffset UpdatedAt);
