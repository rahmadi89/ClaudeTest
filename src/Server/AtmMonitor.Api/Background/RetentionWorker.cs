using AtmMonitor.Contracts;
using AtmMonitor.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AtmMonitor.Api.Background;

/// <summary>Enforces data retention with set-based deletes. Safe to run on several instances concurrently.</summary>
public sealed class RetentionWorker(IServiceScopeFactory scopes, IOptions<RetentionOptions> options, ILogger<RetentionWorker> logger)
    : PeriodicWorker(scopes, logger)
{
    protected override TimeSpan Interval => TimeSpan.FromHours(options.Value.RunEveryHours);

    protected override async Task RunOnceAsync(IServiceProvider sp, CancellationToken ct)
    {
        var db = sp.GetRequiredService<AppDbContext>();
        var now = sp.GetRequiredService<TimeProvider>().GetUtcNow();
        var o = options.Value;

        var telemetryCutoff = now.AddDays(-o.TelemetryDays);
        var logCutoff = now.AddDays(-o.LogDays);
        var alertCutoff = now.AddDays(-o.ResolvedAlertDays);
        var commandCutoff = now.AddDays(-o.CommandDays);
        var auditCutoff = now.AddDays(-o.AuditDays);

        var t = await db.Telemetry.Where(x => x.Timestamp < telemetryCutoff).ExecuteDeleteAsync(ct);
        var l = await db.Logs.Where(x => x.ReceivedAt < logCutoff).ExecuteDeleteAsync(ct);
        var a = await db.Alerts.Where(x => x.Status == AlertStatus.Resolved && x.ResolvedAt < alertCutoff).ExecuteDeleteAsync(ct);
        var c = await db.Commands.Where(x => x.CompletedAt != null && x.CompletedAt < commandCutoff).ExecuteDeleteAsync(ct);
        var u = await db.Audit.Where(x => x.Timestamp < auditCutoff).ExecuteDeleteAsync(ct);

        if (t + l + a + c + u > 0)
        {
            logger.LogInformation("Retention purged telemetry={Telemetry} logs={Logs} alerts={Alerts} commands={Commands} audit={Audit}", t, l, a, c, u);
        }
    }
}
