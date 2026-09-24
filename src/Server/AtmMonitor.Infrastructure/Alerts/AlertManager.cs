using AtmMonitor.Contracts;
using AtmMonitor.Domain.Entities;
using AtmMonitor.Domain.Services;
using AtmMonitor.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AtmMonitor.Infrastructure.Alerts;

/// <summary>
/// Reconciles the alert conditions detected by <see cref="AlertRules"/> with the alerts stored for a terminal:
/// new condition → open alert, persisting condition → bump occurrence, cleared condition → auto-resolve.
/// Changes are staged on the DbContext; the caller saves.
/// </summary>
public sealed class AlertManager(AppDbContext db, IOptions<AlertThresholds> thresholds, TimeProvider clock)
{
    public const string SystemActor = "system";

    /// <summary>Alerts for events (not states). They are never auto-resolved; an operator closes them.</summary>
    public static bool IsEventAlert(AlertType type) => type is AlertType.CommandFailed;

    public async Task<IReadOnlyList<Alert>> ReconcileAsync(Atm atm, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var candidates = AlertRules.Evaluate(atm, thresholds.Value);
        var active = await db.Alerts
            .Where(a => a.AtmId == atm.Id && a.Status != AlertStatus.Resolved)
            .ToListAsync(ct);

        var changed = new List<Alert>();
        var candidateKeys = new HashSet<string>();

        foreach (var c in candidates)
        {
            var dedupKey = $"{c.Type}:{c.DedupKey}";
            if (!candidateKeys.Add(dedupKey))
            {
                continue;
            }

            var existing = active.FirstOrDefault(a => a.DedupKey == dedupKey);
            if (existing is null)
            {
                var alert = new Alert
                {
                    AtmId = atm.Id, Type = c.Type, Severity = c.Severity, DedupKey = dedupKey, Message = c.Message,
                    RaisedAt = now, LastOccurredAt = now,
                };
                db.Alerts.Add(alert);
                changed.Add(alert);
            }
            else
            {
                existing.LastOccurredAt = now;
                existing.OccurrenceCount++;
                if (existing.Message != c.Message || existing.Severity != c.Severity)
                {
                    existing.Message = c.Message;
                    existing.Severity = c.Severity;
                    changed.Add(existing);
                }
            }
        }

        // While a terminal is offline its last reported state is stale — do not auto-resolve device/cash alerts
        // just because the offline rule suppressed them.
        var offline = atm.Status == AtmStatus.Offline;
        foreach (var a in active.Where(a => !candidateKeys.Contains(a.DedupKey) && !IsEventAlert(a.Type)))
        {
            if (offline && a.Type != AlertType.AtmOffline)
            {
                continue;
            }

            if (a.Resolve(SystemActor, "Condition cleared", now))
            {
                changed.Add(a);
            }
        }

        return changed;
    }

    /// <summary>Raises a one-off alert that is not derived from terminal state (e.g. a failed command).</summary>
    public Alert RaiseEvent(Guid atmId, AlertType type, AlertSeverity severity, string message)
    {
        var now = clock.GetUtcNow();
        var alert = new Alert
        {
            AtmId = atmId, Type = type, Severity = severity, Message = message,
            DedupKey = $"{type}:event:{Guid.NewGuid():N}", RaisedAt = now, LastOccurredAt = now,
        };
        db.Alerts.Add(alert);
        return alert;
    }
}
