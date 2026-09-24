using AtmMonitor.Contracts;
using AtmMonitor.Domain.Services;

namespace AtmMonitor.Server.Tests.Domain;

public class AlertRulesTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly AlertThresholds Thresholds = new();

    [Fact]
    public void Healthy_terminal_has_no_alerts()
    {
        var atm = AtmTests.NewAtm();
        atm.ApplyReport(AtmTests.Report(T0), T0);
        Assert.Empty(AlertRules.Evaluate(atm, Thresholds));
    }

    [Fact]
    public void Offline_terminal_only_raises_offline_alert()
    {
        var atm = AtmTests.NewAtm();
        atm.ApplyReport(AtmTests.Report(T0, components: [new(ComponentType.CardReader, ComponentState.Error)]), T0);
        atm.MarkOffline(T0.AddMinutes(5));
        var alert = Assert.Single(AlertRules.Evaluate(atm, Thresholds));
        Assert.Equal(AlertType.AtmOffline, alert.Type);
        Assert.Equal(AlertSeverity.Critical, alert.Severity);
    }

    [Fact]
    public void Cash_rules_distinguish_low_empty_and_fully_empty()
    {
        var atm = AtmTests.NewAtm();
        atm.ApplyReport(AtmTests.Report(T0, cassettes:
        [
            new("C1", CassetteType.Dispense, "USD", 20, 0, 1000, CassetteStatus.Empty),
            new("C2", CassetteType.Dispense, "USD", 50, 30, 1000, CassetteStatus.Low),
            new("R", CassetteType.Reject, "USD", 0, 300, 300, CassetteStatus.Full),
        ]), T0);

        var alerts = AlertRules.Evaluate(atm, Thresholds);
        Assert.Contains(alerts, a => a is { Type: AlertType.CashEmpty, DedupKey: "cassette:C1" });
        Assert.Contains(alerts, a => a is { Type: AlertType.CashLow, DedupKey: "cassette:C2" });
        Assert.Contains(alerts, a => a.Type == AlertType.RejectBinFull);
        Assert.DoesNotContain(alerts, a => a.DedupKey == "cash:all");
    }

    [Fact]
    public void All_dispense_cassettes_empty_is_critical()
    {
        var atm = AtmTests.NewAtm();
        atm.ApplyReport(AtmTests.Report(T0, cassettes: [new("C1", CassetteType.Dispense, "USD", 20, 0, 1000, CassetteStatus.Empty)]), T0);
        Assert.Contains(AlertRules.Evaluate(atm, Thresholds), a => a is { DedupKey: "cash:all", Severity: AlertSeverity.Critical });
    }

    [Fact]
    public void Security_sensors_raise_security_breach()
    {
        var atm = AtmTests.NewAtm();
        atm.ApplyReport(AtmTests.Report(T0, components: [new(ComponentType.SafeDoor, ComponentState.Error, "OPEN", "Safe door open")]), T0);
        var alert = Assert.Single(AlertRules.Evaluate(atm, Thresholds));
        Assert.Equal(AlertType.SecurityBreach, alert.Type);
    }

    [Fact]
    public void Network_rules_cover_unreachable_and_degraded()
    {
        var atm = AtmTests.NewAtm();
        atm.ApplyReport(AtmTests.Report(T0, network: new(false, null, 100, null, "eth0", true, null)), T0);
        Assert.Contains(AlertRules.Evaluate(atm, Thresholds), a => a.Type == AlertType.HostUnreachable);

        atm.ApplyReport(AtmTests.Report(T0.AddSeconds(1), network: new(true, 900, 0, null, "eth0", true, null)), T0);
        Assert.Contains(AlertRules.Evaluate(atm, Thresholds), a => a.Type == AlertType.NetworkDegraded);
    }
}
