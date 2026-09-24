using AtmMonitor.Contracts;

namespace AtmMonitor.Agent.Devices;

public sealed record DeviceSnapshot(
    OperationalMode Mode,
    IReadOnlyList<ComponentStatusDto> Components,
    IReadOnlyList<CassetteDto> Cassettes);

public sealed record DeviceEvent(DateTimeOffset Timestamp, LogSeverity Severity, string Source, string Message);

/// <summary>
/// Hardware abstraction. Production implementations wrap the vendor's CEN/XFS 3.x (msxfs.dll) or XFS4IoT services;
/// the rest of the agent is hardware-agnostic.
/// </summary>
public interface IDeviceProvider
{
    Task<DeviceSnapshot> GetSnapshotAsync(CancellationToken ct);

    /// <summary>Raised when the device layer detects a state change, so the agent can push a report immediately.</summary>
    event EventHandler? StateChanged;

    /// <summary>Device-level events (faults, door opened, cassette removed) to ship as logs.</summary>
    IReadOnlyList<DeviceEvent> DrainEvents();

    Task<string> SetModeAsync(OperationalMode mode, CancellationToken ct);

    Task<string> ResetDeviceAsync(ComponentType device, CancellationToken ct);

    Task<string> SelfTestAsync(CancellationToken ct);
}
