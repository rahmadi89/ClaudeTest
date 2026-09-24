using AtmMonitor.Api.Services;
using AtmMonitor.Contracts;
using AtmMonitor.Infrastructure.Alerts;
using AtmMonitor.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AtmMonitor.Api.Background;

/// <summary>Marks terminals Offline when they have been silent longer than the grace period, and raises the alert.</summary>
public sealed class OfflineWatchdog(IServiceScopeFactory scopes, IOptions<MonitoringOptions> options, ILogger<OfflineWatchdog> logger)
    : PeriodicWorker(scopes, logger)
{
    protected override TimeSpan Interval => TimeSpan.FromSeconds(options.Value.WatchdogIntervalSeconds);

    protected override async Task RunOnceAsync(IServiceProvider sp, CancellationToken ct)
    {
        var db = sp.GetRequiredService<AppDbContext>();
        var clock = sp.GetRequiredService<TimeProvider>();
        var alerts = sp.GetRequiredService<AlertManager>();
        var notifier = sp.GetRequiredService<DashboardNotifier>();

        var now = clock.GetUtcNow();
        var cutoff = now.AddSeconds(-options.Value.OfflineAfterSeconds);
        var stale = await db.Atms
            .Include(a => a.Components)
            .Include(a => a.Cassettes)
            .AsSplitQuery()
            .Where(a => a.IsEnabled && a.AgentKeyHash != null && a.Status != AtmStatus.Offline && a.LastSeenAt != null && a.LastSeenAt < cutoff)
            .OrderBy(a => a.LastSeenAt)
            .Take(500)
            .ToListAsync(ct);

        foreach (var atm in stale)
        {
            atm.MarkOffline(now);
            var changed = await alerts.ReconcileAsync(atm, ct);
            await db.SaveChangesAsync(ct);
            logger.LogWarning("Terminal {TerminalId} marked Offline (last seen {LastSeen})", atm.TerminalId, atm.LastSeenAt);
            await notifier.AtmUpdated(atm);
            await notifier.AlertsChanged(changed, atm.TerminalId);
        }
    }
}
