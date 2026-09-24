using System.Globalization;
using System.Text;
using AtmMonitor.Agent.Configuration;
using AtmMonitor.Agent.Connectivity;
using AtmMonitor.Agent.Devices;
using AtmMonitor.Agent.Logs;
using AtmMonitor.Agent.Monitoring;
using AtmMonitor.Agent.Storage;
using AtmMonitor.Contracts;
using Microsoft.Extensions.Options;

namespace AtmMonitor.Agent.Commands;

/// <summary>Thrown when a command is refused by local policy (not an execution failure).</summary>
public sealed class CommandRejectedException(string message) : Exception(message);

/// <summary>Result of a handler plus an optional action to run <em>after</em> the result has been reported (restart/reboot).</summary>
public sealed record CommandOutcome(string Output, Func<CancellationToken, Task>? After = null);

/// <summary>
/// Executes server commands with local safety rails: expiry check, exactly-once execution (by command id),
/// per-terminal allow-lists for scripts, application restart and reboot.
/// </summary>
public sealed class CommandExecutor(
    Messenger messenger,
    LocalStore store,
    IDeviceProvider devices,
    INetworkProbe network,
    ISystemMetricsCollector system,
    IProcessRunner processes,
    StatusTrigger statusTrigger,
    IHostApplicationLifetime lifetime,
    IOptions<AgentOptions> options,
    TimeProvider clock,
    ILogger<CommandExecutor> logger)
{
    public const int MaxLogLines = 2000;

    public async Task ExecuteAsync(CommandEnvelope cmd, CancellationToken ct)
    {
        using var scope = logger.BeginScope(new Dictionary<string, object> { ["CommandId"] = cmd.CommandId, ["CommandType"] = cmd.Type });

        if (cmd.ExpiresAt <= clock.GetUtcNow())
        {
            logger.LogWarning("Command {Type} expired before execution", cmd.Type);
            await Report(cmd, CommandStatus.Rejected, error: "Command expired before it reached the terminal.", ct: ct);
            return;
        }

        if (!store.TryMarkCommandProcessed(cmd.CommandId))
        {
            logger.LogInformation("Command {CommandId} already processed; ignoring redelivery", cmd.CommandId);
            return;
        }

        logger.LogInformation("Executing {Type}", cmd.Type);
        await Report(cmd, CommandStatus.Acknowledged, ct: ct);

        CommandOutcome outcome;
        try
        {
            await Report(cmd, CommandStatus.Running, ct: ct);
            outcome = await HandleAsync(cmd, ct);
        }
        catch (CommandRejectedException ex)
        {
            logger.LogWarning("Command rejected: {Reason}", ex.Message);
            await Report(cmd, CommandStatus.Rejected, error: ex.Message, ct: ct);
            return;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogError(ex, "Command failed");
            await Report(cmd, CommandStatus.Failed, error: ex.Message, ct: ct);
            return;
        }

        await Report(cmd, CommandStatus.Succeeded, output: outcome.Output, ct: ct);
        if (outcome.After is { } after)
        {
            await after(ct);
        }
    }

    private Task<CommandOutcome> HandleAsync(CommandEnvelope cmd, CancellationToken ct) => cmd.Type switch
    {
        CommandType.Ping => Task.FromResult(new CommandOutcome($"pong from {options.Value.TerminalId} (agent {AgentInfo.Version}) at {clock.GetUtcNow():O}")),
        CommandType.RefreshStatus => RefreshAsync(),
        CommandType.CollectLogs => Task.FromResult(CollectLogs(cmd.Parameters)),
        CommandType.RunDiagnostics => DiagnosticsAsync(ct),
        CommandType.SetOutOfService => SetModeAsync(OperationalMode.OutOfService, ct),
        CommandType.SetInService => SetModeAsync(OperationalMode.InService, ct),
        CommandType.ResetDevice => ResetDeviceAsync(cmd.Parameters, ct),
        CommandType.RestartApplication => RestartApplicationAsync(ct),
        CommandType.RestartAgent => Task.FromResult(RestartAgent()),
        CommandType.RebootMachine => Task.FromResult(Reboot()),
        CommandType.RunScript => RunScriptAsync(cmd.Parameters, ct),
        _ => throw new CommandRejectedException($"Command {cmd.Type} is not supported by agent {AgentInfo.Version}."),
    };

    private Task<CommandOutcome> RefreshAsync()
    {
        statusTrigger.Fire();
        return Task.FromResult(new CommandOutcome("Status refresh requested."));
    }

    private CommandOutcome CollectLogs(IReadOnlyDictionary<string, string> p)
    {
        var lines = p.TryGetValue("lines", out var l) && int.TryParse(l, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
            ? Math.Clamp(n, 1, MaxLogLines) : 200;
        var sourceName = p.GetValueOrDefault("source", "agent");

        string path;
        if (sourceName.Equals("agent", StringComparison.OrdinalIgnoreCase))
        {
            var dir = Path.Combine(options.Value.DataDirectory, "logs");
            path = Directory.Exists(dir)
                ? Directory.GetFiles(dir, "agent*.log").OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault() ?? ""
                : "";
        }
        else
        {
            // Only configured sources can be read – never an arbitrary path supplied by the server.
            path = options.Value.LogSources.FirstOrDefault(s => s.Name.Equals(sourceName, StringComparison.OrdinalIgnoreCase))?.Path
                ?? throw new CommandRejectedException($"Unknown log source '{sourceName}'. Configured: agent, {string.Join(", ", options.Value.LogSources.Select(s => s.Name))}");
        }

        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            throw new InvalidOperationException($"Log file for '{sourceName}' not found.");
        }

        return new CommandOutcome($"== {sourceName}: {path} (last {lines} lines) ==\n{LogCollector.ReadTail(path, lines)}");
    }

    private async Task<CommandOutcome> DiagnosticsAsync(CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine(CultureInfo.InvariantCulture, $"Agent {AgentInfo.Version} on {Environment.MachineName}, terminal {options.Value.TerminalId}");
        var sys = system.Collect();
        sb.AppendLine(CultureInfo.InvariantCulture, $"OS: {sys.OsDescription}; uptime {sys.Uptime:d\\.hh\\:mm}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"CPU {sys.CpuPercent}% | Memory {sys.MemoryUsedPercent}% | Disk {sys.DiskUsedPercent}% ({sys.DiskFreeBytes / 1024 / 1024} MB free)");
        var net = await network.ProbeAsync(ct);
        sb.AppendLine(CultureInfo.InvariantCulture, $"Network: iface {net.InterfaceName} up={net.InterfaceUp} ip={net.LocalIpAddress} speed={net.LinkSpeedMbps}Mbps");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Host: reachable={net.HostReachable} latency={net.HostLatencyMs}ms loss={net.PacketLossPercent}%");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Offline buffer: {store.OutboxCount()} item(s)");
        sb.AppendLine("Devices:");
        sb.AppendLine(await devices.SelfTestAsync(ct));
        return new CommandOutcome(sb.ToString());
    }

    private async Task<CommandOutcome> SetModeAsync(OperationalMode mode, CancellationToken ct)
    {
        var result = await devices.SetModeAsync(mode, ct);
        statusTrigger.Fire();
        return new CommandOutcome(result);
    }

    private async Task<CommandOutcome> ResetDeviceAsync(IReadOnlyDictionary<string, string> p, CancellationToken ct)
    {
        if (!p.TryGetValue("device", out var name) || !Enum.TryParse<ComponentType>(name, ignoreCase: true, out var device))
        {
            throw new CommandRejectedException($"Parameter 'device' must be one of: {string.Join(", ", Enum.GetNames<ComponentType>())}.");
        }

        var result = await devices.ResetDeviceAsync(device, ct);
        statusTrigger.Fire();
        return new CommandOutcome(result);
    }

    private async Task<CommandOutcome> RestartApplicationAsync(CancellationToken ct)
    {
        var app = options.Value.ApplicationControl;
        if (string.IsNullOrWhiteSpace(app.RestartFileName))
        {
            throw new CommandRejectedException("Application restart is not configured on this terminal (Agent:ApplicationControl).");
        }

        var result = await processes.RunAsync(app.RestartFileName, app.RestartArguments, TimeSpan.FromMinutes(5), ct);
        return result.ExitCode == 0 && !result.TimedOut
            ? new CommandOutcome(result.Output)
            : throw new InvalidOperationException($"Restart script exited with {result.ExitCode}{(result.TimedOut ? " (timed out)" : "")}: {result.Output}");
    }

    private CommandOutcome RestartAgent() => new("Agent restarting.", async ct =>
    {
        await messenger.FlushAsync(ct);
        // Non-zero exit makes the service manager restart us (systemd Restart=on-failure, Windows SCM recovery actions).
        Environment.ExitCode = 3;
        lifetime.StopApplication();
    });

    private CommandOutcome Reboot()
    {
        if (!options.Value.AllowReboot)
        {
            throw new CommandRejectedException("Remote reboot is disabled on this terminal (Agent:AllowReboot=false).");
        }

        return new CommandOutcome("Reboot scheduled in 60 seconds.", async ct =>
        {
            await messenger.FlushAsync(ct);
            var (file, args) = OperatingSystem.IsWindows()
                ? ("shutdown.exe", new[] { "/r", "/t", "60", "/c", "Remote reboot requested by ATM Monitor" })
                : ("systemctl", new[] { "reboot" });
            var result = await processes.RunAsync(file, args, TimeSpan.FromSeconds(30), ct);
            if (result.ExitCode != 0)
            {
                logger.LogError("Reboot command failed ({ExitCode}): {Output}", result.ExitCode, result.Output);
            }
        });
    }

    private async Task<CommandOutcome> RunScriptAsync(IReadOnlyDictionary<string, string> p, CancellationToken ct)
    {
        if (!p.TryGetValue("script", out var name) || !options.Value.Scripts.TryGetValue(name, out var script))
        {
            throw new CommandRejectedException($"Script '{name}' is not in this terminal's allow-list.");
        }

        var result = await processes.RunAsync(script.FileName, script.Arguments, TimeSpan.FromSeconds(script.TimeoutSeconds), ct);
        if (result.TimedOut)
        {
            throw new TimeoutException($"Script '{name}' timed out after {script.TimeoutSeconds}s.\n{result.Output}");
        }

        return result.ExitCode == 0
            ? new CommandOutcome(result.Output)
            : throw new InvalidOperationException($"Script '{name}' exited with code {result.ExitCode}.\n{result.Output}");
    }

    private Task Report(CommandEnvelope cmd, CommandStatus status, string? output = null, string? error = null, CancellationToken ct = default) =>
        messenger.SendCommandUpdateAsync(new CommandUpdate(cmd.CommandId, status, output, error, clock.GetUtcNow()), ct);
}

/// <summary>Lets any component request an immediate status report.</summary>
public sealed class StatusTrigger : IDisposable
{
    private readonly SemaphoreSlim _signal = new(0, 1);

    public void Fire()
    {
        if (_signal.CurrentCount == 0)
        {
            try
            {
                _signal.Release();
            }
            catch (SemaphoreFullException)
            {
            }
        }
    }

    public Task<bool> WaitAsync(TimeSpan timeout, CancellationToken ct) => _signal.WaitAsync(timeout, ct);

    public void Dispose() => _signal.Dispose();
}
