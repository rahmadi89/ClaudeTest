using AtmMonitor.Contracts;

namespace AtmMonitor.Domain.Entities;

public class Atm
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Business identifier (the terminal ID configured on the switch). Unique.</summary>
    public required string TerminalId { get; set; }
    public required string Name { get; set; }
    public string? Branch { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public string? Vendor { get; set; }
    public string? Model { get; set; }
    public string? SerialNumber { get; set; }

    public bool IsEnabled { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }

    // Agent identity
    public string? AgentKeyHash { get; set; }
    public DateTimeOffset? EnrolledAt { get; set; }
    public string? AgentVersion { get; set; }
    public string? MachineName { get; set; }
    public string? OsDescription { get; set; }

    // Live state (projection of the latest StatusReport)
    public AtmStatus Status { get; set; } = AtmStatus.Unknown;
    public OperationalMode Mode { get; set; } = OperationalMode.Unknown;
    public DateTimeOffset? LastSeenAt { get; set; }
    public DateTimeOffset? LastReportAt { get; set; }
    public DateTimeOffset? StatusChangedAt { get; set; }
    public bool IsConnected { get; set; }

    public NetworkSnapshot? Network { get; set; }
    public SystemSnapshot? System { get; set; }
    public List<AtmComponent> Components { get; set; } = [];
    public List<Cassette> Cassettes { get; set; } = [];

    /// <summary>Components whose failure takes the terminal out of service.</summary>
    public static readonly IReadOnlySet<ComponentType> CriticalComponents = new HashSet<ComponentType>
    {
        ComponentType.CardReader, ComponentType.CashDispenser, ComponentType.PinPad, ComponentType.Display,
    };

    /// <summary>
    /// Applies a status report as the latest truth. Reports older than the last applied one are ignored
    /// (agents may replay buffered reports after reconnecting).
    /// </summary>
    /// <returns><c>true</c> if the report was applied.</returns>
    public bool ApplyReport(StatusReport report, DateTimeOffset now)
    {
        LastSeenAt = now;
        IsConnected = true;
        if (LastReportAt is { } last && report.CapturedAt <= last)
        {
            return false;
        }

        LastReportAt = report.CapturedAt;
        AgentVersion = report.AgentVersion;
        Mode = report.Mode;

        SyncComponents(report.Components, now);
        SyncCassettes(report.Cassettes, now);

        if (report.Network is { } n)
        {
            Network = new NetworkSnapshot
            {
                HostReachable = n.HostReachable,
                HostLatencyMs = n.HostLatencyMs,
                PacketLossPercent = n.PacketLossPercent,
                LocalIpAddress = n.LocalIpAddress,
                InterfaceName = n.InterfaceName,
                InterfaceUp = n.InterfaceUp,
                LinkSpeedMbps = n.LinkSpeedMbps,
            };
        }

        if (report.System is { } s)
        {
            System = new SystemSnapshot
            {
                CpuPercent = s.CpuPercent,
                MemoryUsedPercent = s.MemoryUsedPercent,
                DiskUsedPercent = s.DiskUsedPercent,
                DiskFreeBytes = s.DiskFreeBytes,
                UptimeSeconds = (long)s.Uptime.TotalSeconds,
                OsDescription = s.OsDescription,
            };
            OsDescription = s.OsDescription;
            MachineName = s.MachineName;
        }

        SetStatus(DeriveStatus(), now);
        return true;
    }

    public void MarkOffline(DateTimeOffset now)
    {
        IsConnected = false;
        SetStatus(AtmStatus.Offline, now);
    }

    public void MarkConnected(DateTimeOffset now)
    {
        IsConnected = true;
        LastSeenAt = now;
        if (Status is AtmStatus.Offline or AtmStatus.Unknown)
        {
            SetStatus(LastReportAt is null ? AtmStatus.Unknown : DeriveStatus(), now);
        }
    }

    public AtmStatus DeriveStatus()
    {
        if (Mode is OperationalMode.Maintenance or OperationalMode.Supervisor)
        {
            return AtmStatus.Maintenance;
        }

        if (Mode == OperationalMode.OutOfService ||
            Components.Any(c => c.State is ComponentState.Error or ComponentState.Offline && CriticalComponents.Contains(c.Type)))
        {
            return AtmStatus.OutOfService;
        }

        var degraded =
            Components.Any(c => c.State is ComponentState.Error or ComponentState.Warning or ComponentState.Offline) ||
            Cassettes.Any(c => c.Status is CassetteStatus.Low or CassetteStatus.Empty or CassetteStatus.Full or CassetteStatus.Missing or CassetteStatus.Inoperative) ||
            Network is { HostReachable: false } ||
            Network is { InterfaceUp: false };

        return degraded ? AtmStatus.Degraded : AtmStatus.Online;
    }

    /// <summary>Total dispensable cash per currency (dispense + recycle cassettes only).</summary>
    public IReadOnlyDictionary<string, decimal> AvailableCash() => Cassettes
        .Where(c => c.Type is CassetteType.Dispense or CassetteType.Recycle && c.Status != CassetteStatus.Missing)
        .GroupBy(c => c.Currency)
        .ToDictionary(g => g.Key, g => g.Sum(c => c.Denomination * c.Count));

    private void SetStatus(AtmStatus status, DateTimeOffset now)
    {
        if (Status != status)
        {
            Status = status;
            StatusChangedAt = now;
        }
    }

    private void SyncComponents(IReadOnlyList<ComponentStatusDto> incoming, DateTimeOffset now)
    {
        foreach (var dto in incoming)
        {
            var existing = Components.FirstOrDefault(c => c.Type == dto.Type);
            if (existing is null)
            {
                Components.Add(new AtmComponent
                {
                    AtmId = Id, Type = dto.Type, State = dto.State, ErrorCode = dto.ErrorCode,
                    Description = dto.Description, UpdatedAt = now, StateChangedAt = now,
                });
                continue;
            }

            if (existing.State != dto.State)
            {
                existing.StateChangedAt = now;
            }

            existing.State = dto.State;
            existing.ErrorCode = dto.ErrorCode;
            existing.Description = dto.Description;
            existing.UpdatedAt = now;
        }

        // Components the agent no longer reports (hardware removed / provider change) are dropped.
        Components.RemoveAll(c => incoming.All(i => i.Type != c.Type));
    }

    private void SyncCassettes(IReadOnlyList<CassetteDto> incoming, DateTimeOffset now)
    {
        foreach (var dto in incoming)
        {
            var existing = Cassettes.FirstOrDefault(c => c.CassetteId == dto.CassetteId);
            if (existing is null)
            {
                existing = new Cassette { AtmId = Id, CassetteId = dto.CassetteId };
                Cassettes.Add(existing);
            }

            existing.Type = dto.Type;
            existing.Currency = dto.Currency;
            existing.Denomination = dto.Denomination;
            existing.Count = dto.Count;
            existing.Capacity = dto.Capacity;
            existing.Status = dto.Status;
            existing.UpdatedAt = now;
        }

        Cassettes.RemoveAll(c => incoming.All(i => i.CassetteId != c.CassetteId));
    }
}

public class AtmComponent
{
    public long Id { get; set; }
    public Guid AtmId { get; set; }
    public ComponentType Type { get; set; }
    public ComponentState State { get; set; }
    public string? ErrorCode { get; set; }
    public string? Description { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset StateChangedAt { get; set; }
}

public class Cassette
{
    public long Id { get; set; }
    public Guid AtmId { get; set; }
    public required string CassetteId { get; set; }
    public CassetteType Type { get; set; }
    public string Currency { get; set; } = "";
    public decimal Denomination { get; set; }
    public int Count { get; set; }
    public int Capacity { get; set; }
    public CassetteStatus Status { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public double FillPercent => Capacity <= 0 ? 0 : Math.Round(100.0 * Count / Capacity, 1);
}

public class NetworkSnapshot
{
    public bool HostReachable { get; set; }
    public double? HostLatencyMs { get; set; }
    public double PacketLossPercent { get; set; }
    public string? LocalIpAddress { get; set; }
    public string? InterfaceName { get; set; }
    public bool InterfaceUp { get; set; }
    public long? LinkSpeedMbps { get; set; }
}

public class SystemSnapshot
{
    public double CpuPercent { get; set; }
    public double MemoryUsedPercent { get; set; }
    public double DiskUsedPercent { get; set; }
    public long DiskFreeBytes { get; set; }
    public long UptimeSeconds { get; set; }
    public string? OsDescription { get; set; }
}
