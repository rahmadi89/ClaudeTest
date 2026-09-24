using AtmMonitor.Contracts;
using AtmMonitor.Domain.Entities;

namespace AtmMonitor.Domain.Services;

/// <summary>Who may issue which remote command. Kept in the domain so API and UI share one rule set.</summary>
public static class CommandPolicy
{
    public static UserRole RequiredRole(CommandType type) => type switch
    {
        CommandType.Ping or CommandType.RefreshStatus or CommandType.CollectLogs or CommandType.RunDiagnostics => UserRole.Operator,
        CommandType.SetInService or CommandType.SetOutOfService or CommandType.ResetDevice or CommandType.RestartAgent => UserRole.Operator,
        CommandType.RestartApplication or CommandType.RebootMachine or CommandType.RunScript => UserRole.Admin,
        _ => UserRole.Admin,
    };

    /// <summary>High-impact commands must carry a reason (captured in the audit trail).</summary>
    public static bool RequiresReason(CommandType type) => type is
        CommandType.RebootMachine or CommandType.RestartApplication or CommandType.RunScript or
        CommandType.SetOutOfService or CommandType.SetInService;

    public static bool CanIssue(UserRole role, CommandType type) => role >= RequiredRole(type);

    public static TimeSpan DefaultTimeout(CommandType type) => type switch
    {
        CommandType.RebootMachine => TimeSpan.FromMinutes(15),
        CommandType.RunDiagnostics or CommandType.CollectLogs or CommandType.RunScript => TimeSpan.FromMinutes(10),
        _ => TimeSpan.FromMinutes(5),
    };
}
