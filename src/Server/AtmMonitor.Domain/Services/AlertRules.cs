using AtmMonitor.Contracts;
using AtmMonitor.Domain.Entities;

namespace AtmMonitor.Domain.Services;

public sealed record AlertThresholds
{
    public double DiskUsedWarningPercent { get; init; } = 90;
    public double PacketLossWarningPercent { get; init; } = 20;
    public double LatencyWarningMs { get; init; } = 500;
}

/// <summary>A condition detected on a terminal. <see cref="DedupKey"/> is stable while the condition persists.</summary>
public sealed record AlertCandidate(AlertType Type, AlertSeverity Severity, string DedupKey, string Message);

/// <summary>
/// Pure, stateless evaluation of a terminal's current state into the set of conditions that should have an active alert.
/// The <c>AlertManager</c> reconciles this set against stored alerts (open new / bump existing / auto-resolve cleared).
/// </summary>
public static class AlertRules
{
    public static IReadOnlyList<AlertCandidate> Evaluate(Atm atm, AlertThresholds thresholds)
    {
        var result = new List<AlertCandidate>();

        if (atm.Status == AtmStatus.Offline)
        {
            // While offline, the last reported state is stale; only the offline alert is meaningful.
            result.Add(new(AlertType.AtmOffline, AlertSeverity.Critical, "offline", $"Terminal {atm.TerminalId} is not reporting."));
            return result;
        }

        if (atm.Mode == OperationalMode.OutOfService)
        {
            result.Add(new(AlertType.OutOfService, AlertSeverity.Major, "mode:oos", $"Terminal {atm.TerminalId} is out of service."));
        }

        foreach (var c in atm.Components)
        {
            var isSecurity = c.Type is ComponentType.SafeDoor or ComponentType.TamperSensor or ComponentType.CabinetDoor;
            var detail = string.IsNullOrWhiteSpace(c.ErrorCode) ? c.Description : $"{c.ErrorCode}: {c.Description}";
            switch (c.State)
            {
                case ComponentState.Error when isSecurity:
                    result.Add(new(AlertType.SecurityBreach, AlertSeverity.Critical, $"security:{c.Type}", $"{c.Type} alarm. {detail}".Trim()));
                    break;
                case ComponentState.Error or ComponentState.Offline:
                    var sev = Atm.CriticalComponents.Contains(c.Type) ? AlertSeverity.Critical : AlertSeverity.Major;
                    result.Add(new(AlertType.ComponentFault, sev, $"component:{c.Type}", $"{c.Type} is {c.State}. {detail}".Trim()));
                    break;
                case ComponentState.Warning:
                    result.Add(new(AlertType.ComponentWarning, AlertSeverity.Warning, $"component:{c.Type}", $"{c.Type} warning. {detail}".Trim()));
                    break;
            }
        }

        foreach (var cas in atm.Cassettes)
        {
            var key = $"cassette:{cas.CassetteId}";
            switch (cas.Type, cas.Status)
            {
                case (CassetteType.Dispense or CassetteType.Recycle, CassetteStatus.Empty):
                    result.Add(new(AlertType.CashEmpty, AlertSeverity.Major, key, $"Cassette {cas.CassetteId} ({cas.Currency} {cas.Denomination}) is empty."));
                    break;
                case (CassetteType.Dispense or CassetteType.Recycle, CassetteStatus.Low):
                    result.Add(new(AlertType.CashLow, AlertSeverity.Warning, key, $"Cassette {cas.CassetteId} ({cas.Currency} {cas.Denomination}) is low: {cas.Count} notes left."));
                    break;
                case (CassetteType.Reject or CassetteType.Retract or CassetteType.Deposit, CassetteStatus.Full):
                    result.Add(new(AlertType.RejectBinFull, AlertSeverity.Major, key, $"{cas.Type} bin {cas.CassetteId} is full."));
                    break;
                case (_, CassetteStatus.Missing or CassetteStatus.Inoperative):
                    result.Add(new(AlertType.ComponentFault, AlertSeverity.Major, key, $"Cassette {cas.CassetteId} is {cas.Status}."));
                    break;
            }
        }

        // With multiple dispense cassettes, the terminal can only dispense nothing if all of them are empty.
        var dispensable = atm.Cassettes.Where(c => c.Type is CassetteType.Dispense or CassetteType.Recycle).ToList();
        if (dispensable.Count > 0 && dispensable.All(c => c.Status is CassetteStatus.Empty or CassetteStatus.Missing or CassetteStatus.Inoperative))
        {
            result.Add(new(AlertType.CashEmpty, AlertSeverity.Critical, "cash:all", $"Terminal {atm.TerminalId} has no dispensable cash."));
        }

        if (atm.Network is { } net)
        {
            if (!net.HostReachable || !net.InterfaceUp)
            {
                result.Add(new(AlertType.HostUnreachable, AlertSeverity.Critical, "network:host",
                    net.InterfaceUp ? "Transaction host is unreachable." : $"Network interface {net.InterfaceName} is down."));
            }
            else if (net.PacketLossPercent >= thresholds.PacketLossWarningPercent ||
                     net.HostLatencyMs >= thresholds.LatencyWarningMs)
            {
                result.Add(new(AlertType.NetworkDegraded, AlertSeverity.Warning, "network:quality",
                    $"Network degraded: latency {net.HostLatencyMs:0} ms, loss {net.PacketLossPercent:0.#}%."));
            }
        }

        if (atm.System is { } sys && sys.DiskUsedPercent >= thresholds.DiskUsedWarningPercent)
        {
            result.Add(new(AlertType.DiskSpaceLow, AlertSeverity.Warning, "system:disk", $"Disk usage is {sys.DiskUsedPercent:0.#}%."));
        }

        return result;
    }
}
