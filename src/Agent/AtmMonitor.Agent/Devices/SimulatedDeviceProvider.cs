using System.Collections.Concurrent;
using AtmMonitor.Contracts;

namespace AtmMonitor.Agent.Devices;

/// <summary>
/// Realistic simulator used for development, demos, load tests and CI. Dispenses cash over time, produces
/// occasional device faults and security events, and reacts to commands the way a real terminal would.
/// </summary>
public sealed class SimulatedDeviceProvider : IDeviceProvider, IDisposable
{
    private readonly Lock _gate = new();
    private readonly Random _rng;
    private readonly ConcurrentQueue<DeviceEvent> _events = new();
    private readonly Timer _timer;
    private readonly Dictionary<ComponentType, (ComponentState State, string? Code, string? Text)> _components;
    private readonly List<SimCassette> _cassettes;
    private OperationalMode _mode = OperationalMode.InService;

    private sealed class SimCassette(string id, CassetteType type, decimal denom, int capacity, int count)
    {
        public string Id { get; } = id;
        public CassetteType Type { get; } = type;
        public decimal Denomination { get; } = denom;
        public int Capacity { get; } = capacity;
        public int Count { get; set; } = count;
        public DateTimeOffset? EmptySince { get; set; }
    }

    public event EventHandler? StateChanged;

    public SimulatedDeviceProvider(int? seed = null, TimeSpan? tick = null)
    {
        _rng = seed is { } s ? new Random(s) : new Random();
        _components = Enum.GetValues<ComponentType>()
            .Where(t => t is not (ComponentType.CashRecycler or ComponentType.Nfc))
            .ToDictionary(t => t, _ => (ComponentState.Ok, (string?)null, (string?)null));
        _cassettes =
        [
            new("CST1", CassetteType.Dispense, 20m, 2500, _rng.Next(600, 2500)),
            new("CST2", CassetteType.Dispense, 50m, 2500, _rng.Next(600, 2500)),
            new("CST3", CassetteType.Dispense, 100m, 2000, _rng.Next(400, 2000)),
            new("REJ", CassetteType.Reject, 0m, 300, _rng.Next(0, 50)),
        ];
        var interval = tick ?? TimeSpan.FromSeconds(5);
        _timer = new Timer(_ => Tick(), null, interval, interval);
    }

    public Task<DeviceSnapshot> GetSnapshotAsync(CancellationToken ct)
    {
        lock (_gate)
        {
            var components = _components.Select(kv => new ComponentStatusDto(kv.Key, kv.Value.State, kv.Value.Code, kv.Value.Text)).ToList();
            var cassettes = _cassettes.Select(c => new CassetteDto(c.Id, c.Type, "USD", c.Denomination, c.Count, c.Capacity, StatusOf(c))).ToList();
            return Task.FromResult(new DeviceSnapshot(_mode, components, cassettes));
        }
    }

    public IReadOnlyList<DeviceEvent> DrainEvents()
    {
        var list = new List<DeviceEvent>();
        while (_events.TryDequeue(out var e))
        {
            list.Add(e);
        }

        return list;
    }

    public Task<string> SetModeAsync(OperationalMode mode, CancellationToken ct)
    {
        lock (_gate)
        {
            _mode = mode;
        }

        Emit(LogSeverity.Information, "Application", $"Mode changed to {mode} by remote command");
        return Task.FromResult($"Terminal mode is now {mode}.");
    }

    public Task<string> ResetDeviceAsync(ComponentType device, CancellationToken ct)
    {
        lock (_gate)
        {
            if (!_components.ContainsKey(device))
            {
                throw new InvalidOperationException($"Device {device} is not present on this terminal.");
            }

            _components[device] = (ComponentState.Ok, null, null);
        }

        Emit(LogSeverity.Information, device.ToString(), "Device reset completed");
        return Task.FromResult($"{device} reset OK.");
    }

    public Task<string> SelfTestAsync(CancellationToken ct)
    {
        lock (_gate)
        {
            return Task.FromResult(string.Join(Environment.NewLine,
                _components.Select(kv => $"{kv.Key,-16} {kv.Value.State,-8} {kv.Value.Code} {kv.Value.Text}".TrimEnd())));
        }
    }

    public void Dispose() => _timer.Dispose();

    private void Tick()
    {
        var changed = false;
        lock (_gate)
        {
            if (_mode == OperationalMode.InService && _components[ComponentType.CashDispenser].State == ComponentState.Ok)
            {
                // A withdrawal every tick or so: 1–10 notes from a random dispense cassette.
                var candidates = _cassettes.Where(c => c.Type == CassetteType.Dispense && c.Count > 0).ToList();
                if (candidates.Count > 0 && _rng.NextDouble() < 0.7)
                {
                    var c = candidates[_rng.Next(candidates.Count)];
                    var before = StatusOf(c);
                    c.Count = Math.Max(0, c.Count - _rng.Next(1, 11));
                    if (_rng.NextDouble() < 0.05)
                    {
                        _cassettes.First(x => x.Type == CassetteType.Reject).Count++;
                    }

                    if (StatusOf(c) != before)
                    {
                        changed = true;
                        Emit(StatusOf(c) == CassetteStatus.Empty ? LogSeverity.Error : LogSeverity.Warning, "CashDispenser", $"Cassette {c.Id} is now {StatusOf(c)} ({c.Count} notes)");
                        if (StatusOf(c) == CassetteStatus.Empty)
                        {
                            c.EmptySince = DateTimeOffset.UtcNow;
                        }
                    }
                }
            }

            // Simulated cash-in-transit replenishment a few minutes after a cassette runs empty.
            foreach (var c in _cassettes.Where(c => c.EmptySince is { } t && DateTimeOffset.UtcNow - t > TimeSpan.FromMinutes(3)))
            {
                c.Count = c.Capacity;
                c.EmptySince = null;
                changed = true;
                Emit(LogSeverity.Information, "CashDispenser", $"Cassette {c.Id} replenished to {c.Capacity} notes");
            }

            changed |= MaybeFault(ComponentType.ReceiptPrinter, 0.01, ComponentState.Warning, "PAPER_LOW", "Receipt paper low");
            changed |= MaybeFault(ComponentType.CardReader, 0.003, ComponentState.Error, "CR_JAM", "Card jammed in reader");
            changed |= MaybeFault(ComponentType.CabinetDoor, 0.001, ComponentState.Error, "DOOR_OPEN", "Cabinet door opened outside maintenance window");

            // Transient faults clear on their own sometimes (paper refilled, jam cleared by staff).
            foreach (var type in _components.Keys.ToList())
            {
                if (_components[type].State != ComponentState.Ok && _rng.NextDouble() < 0.02)
                {
                    _components[type] = (ComponentState.Ok, null, null);
                    Emit(LogSeverity.Information, type.ToString(), "Fault cleared");
                    changed = true;
                }
            }
        }

        if (changed)
        {
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private bool MaybeFault(ComponentType type, double probability, ComponentState state, string code, string text)
    {
        if (_components[type].State != ComponentState.Ok || _rng.NextDouble() >= probability)
        {
            return false;
        }

        _components[type] = (state, code, text);
        Emit(state == ComponentState.Error ? LogSeverity.Error : LogSeverity.Warning, type.ToString(), $"{code}: {text}");
        return true;
    }

    private void Emit(LogSeverity severity, string source, string message) =>
        _events.Enqueue(new DeviceEvent(DateTimeOffset.UtcNow, severity, source, message));

    private static CassetteStatus StatusOf(SimCassette c)
    {
        var fill = c.Capacity == 0 ? 0 : (double)c.Count / c.Capacity;
        return c.Type switch
        {
            CassetteType.Reject or CassetteType.Retract => fill >= 1 ? CassetteStatus.Full : fill >= 0.8 ? CassetteStatus.High : CassetteStatus.Ok,
            _ => c.Count == 0 ? CassetteStatus.Empty : fill < 0.1 ? CassetteStatus.Low : CassetteStatus.Ok,
        };
    }
}
