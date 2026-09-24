using System.Collections.Concurrent;
using AtmMonitor.Contracts;
using AtmMonitor.Domain.Entities;
using AtmMonitor.Infrastructure.Alerts;
using AtmMonitor.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AtmMonitor.Api.Services;

public sealed class StatusIngestionService(
    AppDbContext db,
    AlertManager alerts,
    DashboardNotifier notifier,
    TelemetryThrottle throttle,
    IOptions<MonitoringOptions> options,
    TimeProvider clock,
    ILogger<StatusIngestionService> logger)
{
    public async Task IngestStatusAsync(Guid atmId, StatusReport report, CancellationToken ct)
    {
        var atm = await LoadAsync(atmId, ct);
        if (atm is null)
        {
            return;
        }

        var now = clock.GetUtcNow();
        // Guard against agents with a badly skewed clock: never accept reports from the future.
        if (report.CapturedAt > now.AddMinutes(5))
        {
            logger.LogWarning("Status report from {TerminalId} is {Skew} ahead of server time; clamping", atm.TerminalId, report.CapturedAt - now);
            report = report with { CapturedAt = now };
        }

        var previousStatus = atm.Status;
        var previousMode = atm.Mode;
        var applied = atm.ApplyReport(report, now);

        if (applied && throttle.ShouldSample(atmId, now, TimeSpan.FromSeconds(options.Value.TelemetrySampleSeconds)))
        {
            db.Telemetry.Add(new TelemetrySample
            {
                AtmId = atmId,
                Timestamp = report.CapturedAt,
                CpuPercent = report.System?.CpuPercent,
                MemoryUsedPercent = report.System?.MemoryUsedPercent,
                DiskUsedPercent = report.System?.DiskUsedPercent,
                HostLatencyMs = report.Network?.HostLatencyMs,
                PacketLossPercent = report.Network?.PacketLossPercent,
                HostReachable = report.Network?.HostReachable,
                AvailableCash = atm.AvailableCash().Values.Sum(),
            });
        }

        var changedAlerts = applied ? await alerts.ReconcileAsync(atm, ct) : [];
        await db.SaveChangesAsync(ct);

        if (applied && (previousStatus != atm.Status || previousMode != atm.Mode))
        {
            logger.LogInformation("Terminal {TerminalId} status {From} -> {To}", atm.TerminalId, previousStatus, atm.Status);
        }

        await notifier.AtmUpdated(atm);
        if (changedAlerts.Count > 0)
        {
            await notifier.AlertsChanged(changedAlerts, atm.TerminalId);
        }
    }

    public async Task IngestLogsAsync(Guid atmId, LogBatch batch, CancellationToken ct)
    {
        var max = options.Value.MaxLogEntriesPerBatch;
        if (batch.Entries.Count > max)
        {
            throw new Microsoft.AspNetCore.SignalR.HubException($"Log batch exceeds {max} entries.");
        }

        var now = clock.GetUtcNow();
        foreach (var e in batch.Entries)
        {
            db.Logs.Add(new AtmLogEntry
            {
                AtmId = atmId,
                Timestamp = e.Timestamp > now.AddMinutes(5) ? now : e.Timestamp,
                ReceivedAt = now,
                Severity = e.Severity,
                Source = Clip(e.Source, 128),
                Message = Clip(e.Message, 8192),
            });
        }

        await db.Atms.Where(a => a.Id == atmId).ExecuteUpdateAsync(s => s.SetProperty(a => a.LastSeenAt, now), ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task MarkConnectedAsync(Guid atmId, CancellationToken ct)
    {
        var atm = await LoadAsync(atmId, ct);
        if (atm is null)
        {
            return;
        }

        atm.MarkConnected(clock.GetUtcNow());
        var changed = await alerts.ReconcileAsync(atm, ct);
        await db.SaveChangesAsync(ct);
        await notifier.AtmUpdated(atm);
        await notifier.AlertsChanged(changed, atm.TerminalId);
    }

    public async Task MarkDisconnectedAsync(Guid atmId, CancellationToken ct)
    {
        // Do not flip to Offline here: brief network blips reconnect within seconds.
        // The OfflineWatchdog marks Offline once the grace period (Monitoring:OfflineAfterSeconds) elapses.
        await db.Atms.Where(a => a.Id == atmId).ExecuteUpdateAsync(s => s.SetProperty(a => a.IsConnected, false), ct);
    }

    private Task<Atm?> LoadAsync(Guid atmId, CancellationToken ct) => db.Atms
        .Include(a => a.Components)
        .Include(a => a.Cassettes)
        .AsSplitQuery()
        .FirstOrDefaultAsync(a => a.Id == atmId, ct);

    private static string Clip(string? s, int max) => string.IsNullOrEmpty(s) ? "" : s.Length <= max ? s : s[..max];
}

/// <summary>Per-instance throttle so high-frequency status reports do not flood the telemetry table.</summary>
public sealed class TelemetryThrottle
{
    private readonly ConcurrentDictionary<Guid, DateTimeOffset> _last = new();

    public bool ShouldSample(Guid atmId, DateTimeOffset now, TimeSpan minInterval)
    {
        var shouldSample = false;
        _last.AddOrUpdate(atmId,
            _ => { shouldSample = true; return now; },
            (_, last) =>
            {
                if (now - last >= minInterval)
                {
                    shouldSample = true;
                    return now;
                }

                return last;
            });
        return shouldSample;
    }
}
