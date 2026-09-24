using AtmMonitor.Agent.Devices;
using AtmMonitor.Contracts;

namespace AtmMonitor.Agent.Monitoring;

/// <summary>Builds a complete <see cref="StatusReport"/>. Each source fails independently so one broken probe never blanks the report.</summary>
public sealed class StatusCollector(
    IDeviceProvider devices, INetworkProbe network, ISystemMetricsCollector system, TimeProvider clock, ILogger<StatusCollector> logger)
{
    public async Task<StatusReport> CollectAsync(CancellationToken ct)
    {
        DeviceSnapshot? snapshot = null;
        NetworkStatusDto? net = null;
        SystemMetricsDto? sys = null;

        try
        {
            snapshot = await devices.GetSnapshotAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Device snapshot failed");
        }

        try
        {
            net = await network.ProbeAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Network probe failed");
        }

        try
        {
            sys = system.Collect();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "System metrics failed");
        }

        return new StatusReport
        {
            CapturedAt = clock.GetUtcNow(),
            AgentVersion = AgentInfo.Version,
            Mode = snapshot?.Mode ?? OperationalMode.Unknown,
            Components = snapshot?.Components ?? [],
            Cassettes = snapshot?.Cassettes ?? [],
            Network = net,
            System = sys,
        };
    }
}
