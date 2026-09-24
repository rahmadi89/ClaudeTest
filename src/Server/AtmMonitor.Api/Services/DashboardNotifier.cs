using AtmMonitor.Api.Hubs;
using AtmMonitor.Contracts;
using AtmMonitor.Domain.Entities;
using Microsoft.AspNetCore.SignalR;

namespace AtmMonitor.Api.Services;

/// <summary>Fire-and-forget fan-out of state changes to web consoles. Failures never affect the write path.</summary>
public sealed class DashboardNotifier(IHubContext<DashboardHub, IDashboardClient> hub, ILogger<DashboardNotifier> logger)
{
    public Task AtmUpdated(Atm atm) => Safe(() =>
        hub.Clients.All.AtmUpdated(new AtmSummaryEvent(atm.Id, atm.TerminalId, atm.Status, atm.Mode, atm.LastSeenAt)));

    public Task AlertsChanged(IEnumerable<Alert> alerts, string terminalId) => Safe(async () =>
    {
        foreach (var a in alerts)
        {
            await hub.Clients.All.AlertChanged(new AlertEvent(a.Id, a.AtmId, terminalId, a.Type, a.Severity, a.Status, a.Message, a.RaisedAt));
        }
    });

    public Task CommandChanged(AtmCommand c) => Safe(() =>
        hub.Clients.All.CommandChanged(new CommandEvent(c.Id, c.AtmId, c.Type, c.Status, c.UpdatedAt)));

    private async Task Safe(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Dashboard notification failed");
        }
    }
}
