using System.Text.Json;
using AtmMonitor.Agent.Commands;
using AtmMonitor.Agent.Configuration;
using AtmMonitor.Agent.Connectivity;
using AtmMonitor.Agent.Devices;
using AtmMonitor.Agent.Identity;
using AtmMonitor.Agent.Monitoring;
using AtmMonitor.Agent.Storage;
using AtmMonitor.Contracts;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AtmMonitor.Agent.Tests;

/// <summary>
/// Wires a real <see cref="CommandExecutor"/> with a disconnected <see cref="ServerConnection"/>: every report the executor
/// makes lands in the durable outbox, which is exactly what the tests inspect.
/// </summary>
public sealed class TestHarness : IDisposable
{
    public string Dir { get; } = Path.Combine(Path.GetTempPath(), "atm-agent-tests", Guid.NewGuid().ToString("N"));
    public AgentOptions Options { get; } = new() { ServerUrl = "https://server.test", TerminalId = "T1" };
    public LocalStore Store { get; }
    public SimulatedDeviceProvider Devices { get; } = new(seed: 42, tick: TimeSpan.FromHours(1));
    public FakeProcessRunner Processes { get; } = new();
    public CommandExecutor Executor { get; }
    public Messenger Messenger { get; }
    public StatusTrigger Trigger { get; } = new();

    public TestHarness(Action<AgentOptions>? configure = null)
    {
        Directory.CreateDirectory(Dir);
        Options.DataDirectory = Dir;
        configure?.Invoke(Options);
        var opts = Microsoft.Extensions.Options.Options.Create(Options);
        Store = new LocalStore(Path.Combine(Dir, "agent.db"), 1000);
        var creds = new FileCredentialStore(Dir, NullLogger<FileCredentialStore>.Instance);
        var enrollment = new EnrollmentClient(new HttpClient(), opts, creds, NullLogger<EnrollmentClient>.Instance);
        var connection = new ServerConnection(opts, enrollment, creds, NullLogger<ServerConnection>.Instance);
        Messenger = new Messenger(connection, Store, NullLogger<Messenger>.Instance);
        Executor = new CommandExecutor(Messenger, Store, Devices, new FakeNetworkProbe(), new FakeSystemMetrics(), Processes, Trigger,
            new FakeLifetime(), opts, TimeProvider.System, NullLogger<CommandExecutor>.Instance);
    }

    public IReadOnlyList<CommandUpdate> Updates() => Store.Peek(1000)
        .Where(i => i.Kind == AgentHubMethods.ReportCommandUpdate)
        .Select(i => JsonSerializer.Deserialize<CommandUpdate>(i.Payload, Messenger.Json)!)
        .ToList();

    public static CommandEnvelope Command(CommandType type, Dictionary<string, string>? parameters = null, TimeSpan? ttl = null) =>
        new(Guid.NewGuid(), type, parameters ?? [], DateTimeOffset.UtcNow, DateTimeOffset.UtcNow + (ttl ?? TimeSpan.FromMinutes(5)));

    public void Dispose()
    {
        Store.Dispose();
        Devices.Dispose();
        Messenger.Dispose();
        Trigger.Dispose();
        try
        {
            Directory.Delete(Dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}

public sealed class FakeProcessRunner : IProcessRunner
{
    public List<(string File, string[] Args)> Calls { get; } = [];
    public ProcessResult Result { get; set; } = new(0, "ok", false);

    public Task<ProcessResult> RunAsync(string fileName, IEnumerable<string> arguments, TimeSpan timeout, CancellationToken ct)
    {
        Calls.Add((fileName, arguments.ToArray()));
        return Task.FromResult(Result);
    }
}

public sealed class FakeNetworkProbe : INetworkProbe
{
    public Task<NetworkStatusDto> ProbeAsync(CancellationToken ct) => Task.FromResult(new NetworkStatusDto(true, 5, 0, "10.0.0.2", "eth0", true, 1000));
}

public sealed class FakeSystemMetrics : ISystemMetricsCollector
{
    public SystemMetricsDto Collect() => new(5, 30, 40, 10_000_000_000, TimeSpan.FromHours(1), "TestOS", "test");
}

public sealed class FakeLifetime : IHostApplicationLifetime
{
    public CancellationToken ApplicationStarted => CancellationToken.None;
    public CancellationToken ApplicationStopping => CancellationToken.None;
    public CancellationToken ApplicationStopped => CancellationToken.None;
    public bool StopRequested { get; private set; }
    public void StopApplication() => StopRequested = true;
}
