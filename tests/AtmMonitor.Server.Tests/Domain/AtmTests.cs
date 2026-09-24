using AtmMonitor.Contracts;
using AtmMonitor.Domain.Entities;

namespace AtmMonitor.Server.Tests.Domain;

public class AtmTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    internal static StatusReport Report(DateTimeOffset at, OperationalMode mode = OperationalMode.InService,
        ComponentStatusDto[]? components = null, CassetteDto[]? cassettes = null, NetworkStatusDto? network = null) => new()
    {
        CapturedAt = at,
        AgentVersion = "1.0.0",
        Mode = mode,
        Components = components ?? [new(ComponentType.CardReader, ComponentState.Ok)],
        Cassettes = cassettes ?? [new("C1", CassetteType.Dispense, "USD", 20, 1000, 2000, CassetteStatus.Ok)],
        Network = network ?? new(true, 10, 0, "10.0.0.5", "eth0", true, 1000),
    };

    internal static Atm NewAtm() => new() { TerminalId = "T1", Name = "Test" };

    [Fact]
    public void Healthy_report_makes_terminal_online()
    {
        var atm = NewAtm();
        Assert.True(atm.ApplyReport(Report(T0), T0));
        Assert.Equal(AtmStatus.Online, atm.Status);
        Assert.Equal(20_000m, atm.AvailableCash()["USD"]);
    }

    [Fact]
    public void Stale_report_is_ignored_but_counts_as_liveness()
    {
        var atm = NewAtm();
        atm.ApplyReport(Report(T0, OperationalMode.OutOfService), T0);
        var applied = atm.ApplyReport(Report(T0.AddSeconds(-30)), T0.AddSeconds(5));
        Assert.False(applied);
        Assert.Equal(OperationalMode.OutOfService, atm.Mode);
        Assert.Equal(T0.AddSeconds(5), atm.LastSeenAt);
    }

    [Fact]
    public void Critical_component_error_means_out_of_service()
    {
        var atm = NewAtm();
        atm.ApplyReport(Report(T0, components: [new(ComponentType.CashDispenser, ComponentState.Error, "E1", "Jam")]), T0);
        Assert.Equal(AtmStatus.OutOfService, atm.Status);
    }

    [Fact]
    public void Non_critical_warning_or_low_cash_means_degraded()
    {
        var atm = NewAtm();
        atm.ApplyReport(Report(T0, components: [new(ComponentType.ReceiptPrinter, ComponentState.Warning)]), T0);
        Assert.Equal(AtmStatus.Degraded, atm.Status);

        atm.ApplyReport(Report(T0.AddSeconds(1), cassettes: [new("C1", CassetteType.Dispense, "USD", 20, 10, 2000, CassetteStatus.Low)]), T0);
        Assert.Equal(AtmStatus.Degraded, atm.Status);
    }

    [Fact]
    public void Maintenance_mode_wins_over_faults()
    {
        var atm = NewAtm();
        atm.ApplyReport(Report(T0, OperationalMode.Supervisor, components: [new(ComponentType.CardReader, ComponentState.Error)]), T0);
        Assert.Equal(AtmStatus.Maintenance, atm.Status);
    }

    [Fact]
    public void Components_and_cassettes_are_synced_not_appended()
    {
        var atm = NewAtm();
        atm.ApplyReport(Report(T0, cassettes: [new("C1", CassetteType.Dispense, "USD", 20, 5, 10, CassetteStatus.Ok), new("C2", CassetteType.Reject, "USD", 0, 1, 10, CassetteStatus.Ok)]), T0);
        atm.ApplyReport(Report(T0.AddSeconds(1), cassettes: [new("C1", CassetteType.Dispense, "USD", 20, 4, 10, CassetteStatus.Ok)]), T0);
        var only = Assert.Single(atm.Cassettes);
        Assert.Equal(4, only.Count);
    }

    [Fact]
    public void Status_change_timestamp_only_moves_on_change()
    {
        var atm = NewAtm();
        atm.ApplyReport(Report(T0), T0);
        atm.ApplyReport(Report(T0.AddMinutes(1)), T0.AddMinutes(1));
        Assert.Equal(T0, atm.StatusChangedAt);
        atm.MarkOffline(T0.AddMinutes(5));
        Assert.Equal(AtmStatus.Offline, atm.Status);
        Assert.Equal(T0.AddMinutes(5), atm.StatusChangedAt);
    }
}
