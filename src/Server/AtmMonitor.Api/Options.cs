using System.ComponentModel.DataAnnotations;

namespace AtmMonitor.Api;

public sealed class JwtOptions
{
    public const string Section = "Jwt";

    [Required] public string Issuer { get; set; } = "atm-monitor";
    [Required] public string Audience { get; set; } = "atm-monitor";

    /// <summary>HMAC-SHA256 signing key. Must be at least 32 bytes. Supply via secret store / env var, never appsettings.</summary>
    [Required, MinLength(32)] public string SigningKey { get; set; } = "";

    [Range(5, 24 * 60)] public int AccessTokenMinutes { get; set; } = 480;
}

public sealed class MonitoringOptions
{
    public const string Section = "Monitoring";

    /// <summary>A terminal with no traffic for this long is marked Offline.</summary>
    [Range(15, 3600)] public int OfflineAfterSeconds { get; set; } = 120;

    [Range(5, 600)] public int WatchdogIntervalSeconds { get; set; } = 15;

    /// <summary>Minimum spacing of stored telemetry points per terminal (status reports can be more frequent).</summary>
    [Range(0, 3600)] public int TelemetrySampleSeconds { get; set; } = 60;

    [Range(1, 5000)] public int MaxLogEntriesPerBatch { get; set; } = 500;
}

public sealed class RetentionOptions
{
    public const string Section = "Retention";
    [Range(1, 3650)] public int TelemetryDays { get; set; } = 30;
    [Range(1, 3650)] public int LogDays { get; set; } = 90;
    [Range(1, 3650)] public int ResolvedAlertDays { get; set; } = 180;
    [Range(1, 3650)] public int CommandDays { get; set; } = 180;
    /// <summary>Audit records are usually subject to regulatory retention; default is long.</summary>
    [Range(30, 3650)] public int AuditDays { get; set; } = 2555;
    [Range(1, 168)] public int RunEveryHours { get; set; } = 6;
}
