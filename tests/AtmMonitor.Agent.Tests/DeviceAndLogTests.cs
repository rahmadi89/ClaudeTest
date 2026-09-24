using AtmMonitor.Agent.Configuration;
using AtmMonitor.Agent.Devices;
using AtmMonitor.Agent.Logs;
using AtmMonitor.Agent.Storage;
using AtmMonitor.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AtmMonitor.Agent.Tests;

public class DeviceAndLogTests
{
    [Fact]
    public async Task Simulator_reports_full_hardware_inventory()
    {
        using var sim = new SimulatedDeviceProvider(seed: 1, tick: TimeSpan.FromHours(1));
        var snap = await sim.GetSnapshotAsync(CancellationToken.None);
        Assert.Equal(OperationalMode.InService, snap.Mode);
        Assert.Contains(snap.Components, c => c.Type == ComponentType.CashDispenser);
        Assert.Contains(snap.Cassettes, c => c.Type == CassetteType.Reject);
        Assert.All(snap.Cassettes, c => Assert.InRange(c.Count, 0, c.Capacity));
    }

    [Fact]
    public async Task Log_tail_ships_only_new_lines_and_classifies_severity()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"atm-logs-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "journal.log");
        await File.WriteAllTextAsync(file, "old line before agent start\n");

        var options = Options.Create(new AgentOptions
        {
            ServerUrl = "https://x", TerminalId = "T", DataDirectory = dir,
            LogSources = [new LogSourceOptions { Name = "journal", Path = file }],
        });
        using var store = new LocalStore(Path.Combine(dir, "a.db"), 100);
        using var devices = new SimulatedDeviceProvider(seed: 1, tick: TimeSpan.FromHours(1));
        var collector = new LogCollector(devices, store, options, NullLogger<LogCollector>.Instance);

        Assert.DoesNotContain(collector.Collect(), e => e.Source == "journal"); // first sight: start at end
        await File.AppendAllTextAsync(file, "TXN 1234 approved\nDISPENSER ERROR code 55\nwarning: paper low\n");

        var entries = collector.Collect().Where(e => e.Source == "journal").ToList();
        Assert.Equal(3, entries.Count);
        Assert.Equal([LogSeverity.Information, LogSeverity.Error, LogSeverity.Warning], entries.Select(e => e.Severity));
        Assert.DoesNotContain(collector.Collect(), e => e.Source == "journal"); // nothing new

        // Rotation: file replaced with shorter content → restart from beginning.
        await File.WriteAllTextAsync(file, "new file\n");
        Assert.Single(collector.Collect(), e => e.Source == "journal");

        Directory.Delete(dir, recursive: true);
    }
}
