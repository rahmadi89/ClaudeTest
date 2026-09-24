using System.Threading.Channels;
using AtmMonitor.Agent.Commands;
using AtmMonitor.Agent.Configuration;
using AtmMonitor.Agent.Connectivity;
using AtmMonitor.Agent.Devices;
using AtmMonitor.Agent.Logs;
using AtmMonitor.Agent.Monitoring;
using AtmMonitor.Contracts;
using Microsoft.Extensions.Options;

namespace AtmMonitor.Agent.Workers;

/// <summary>Keeps the server connection alive and wires connection events to the other workers.</summary>
public sealed class ConnectionWorker(
    ServerConnection connection, Messenger messenger, StatusTrigger statusTrigger, Channel<CommandEnvelope> commands,
    IHostApplicationLifetime lifetime, ILogger<ConnectionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        connection.OnCommand = cmd => commands.Writer.WriteAsync(cmd, stoppingToken).AsTask();
        connection.OnConnected = async ct =>
        {
            statusTrigger.Fire();
            await messenger.FlushAsync(ct);
        };

        try
        {
            await connection.RunAsync(stoppingToken);
        }
        catch (InvalidOperationException ex)
        {
            // Fatal configuration error: stop with a non-zero code so the service manager surfaces it.
            logger.LogCritical("{Error}", ex.Message);
            Environment.ExitCode = 2;
            lifetime.StopApplication();
        }
    }
}

/// <summary>Pushes a status report on a fixed interval, immediately on device state changes, and on request.</summary>
public sealed class StatusWorker(
    StatusCollector collector, Messenger messenger, StatusTrigger trigger, IDeviceProvider devices,
    IOptions<AgentOptions> options, ILogger<StatusWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        devices.StateChanged += (_, _) => trigger.Fire();
        var interval = TimeSpan.FromSeconds(options.Value.StatusIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var report = await collector.CollectAsync(stoppingToken);
                if (!await messenger.SendStatusAsync(report, stoppingToken))
                {
                    logger.LogDebug("Status not sent (offline)");
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Status cycle failed");
            }

            try
            {
                await trigger.WaitAsync(interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}

public sealed class LogShippingWorker(LogCollector collector, Messenger messenger, IOptions<AgentOptions> options, ILogger<LogShippingWorker> logger)
    : BackgroundService
{
    private const int BatchSize = 500;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(options.Value.LogShipIntervalSeconds));
        do
        {
            try
            {
                var entries = collector.Collect();
                foreach (var chunk in entries.Chunk(BatchSize))
                {
                    await messenger.SendLogsAsync(new LogBatch(chunk), stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Log shipping cycle failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}

/// <summary>Executes commands one at a time – ATM devices are not safe to drive concurrently.</summary>
public sealed class CommandWorker(Channel<CommandEnvelope> commands, CommandExecutor executor, ILogger<CommandWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var cmd in commands.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await executor.ExecuteAsync(cmd, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unexpected error executing command {CommandId}", cmd.CommandId);
            }
        }
    }
}
