using AtmMonitor.Agent.Configuration;
using AtmMonitor.Contracts;

namespace AtmMonitor.Agent.Tests;

public class CommandExecutorTests
{
    [Fact]
    public async Task Ping_reports_acknowledged_running_succeeded_in_order()
    {
        using var h = new TestHarness();
        await h.Executor.ExecuteAsync(TestHarness.Command(CommandType.Ping), CancellationToken.None);
        Assert.Equal([CommandStatus.Acknowledged, CommandStatus.Running, CommandStatus.Succeeded], h.Updates().Select(u => u.Status));
        Assert.Contains("pong", h.Updates()[^1].Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Redelivered_command_is_executed_only_once()
    {
        using var h = new TestHarness(o => o.Scripts["s"] = new ScriptOptions { FileName = "tool" });
        var cmd = TestHarness.Command(CommandType.RunScript, new() { ["script"] = "s" });
        await h.Executor.ExecuteAsync(cmd, CancellationToken.None);
        await h.Executor.ExecuteAsync(cmd, CancellationToken.None);
        Assert.Single(h.Processes.Calls);
    }

    [Fact]
    public async Task Expired_command_is_rejected_without_running()
    {
        using var h = new TestHarness();
        await h.Executor.ExecuteAsync(TestHarness.Command(CommandType.Ping, ttl: TimeSpan.FromSeconds(-1)), CancellationToken.None);
        var update = Assert.Single(h.Updates());
        Assert.Equal(CommandStatus.Rejected, update.Status);
    }

    [Fact]
    public async Task Reboot_is_rejected_unless_enabled_locally()
    {
        using var h = new TestHarness();
        await h.Executor.ExecuteAsync(TestHarness.Command(CommandType.RebootMachine), CancellationToken.None);
        Assert.Equal(CommandStatus.Rejected, h.Updates()[^1].Status);
        Assert.Empty(h.Processes.Calls);
    }

    [Fact]
    public async Task Reboot_runs_after_success_is_reported_when_enabled()
    {
        using var h = new TestHarness(o => o.AllowReboot = true);
        await h.Executor.ExecuteAsync(TestHarness.Command(CommandType.RebootMachine), CancellationToken.None);
        Assert.Equal(CommandStatus.Succeeded, h.Updates()[^1].Status);
        var call = Assert.Single(h.Processes.Calls);
        Assert.True(call.File is "shutdown.exe" or "systemctl");
    }

    [Fact]
    public async Task Only_allow_listed_scripts_run_with_configured_arguments()
    {
        using var h = new TestHarness(o => o.Scripts["cleanup"] = new ScriptOptions { FileName = "/opt/atm/cleanup.sh", Arguments = ["--safe"] });

        await h.Executor.ExecuteAsync(TestHarness.Command(CommandType.RunScript, new() { ["script"] = "rm -rf /" }), CancellationToken.None);
        Assert.Equal(CommandStatus.Rejected, h.Updates()[^1].Status);
        Assert.Empty(h.Processes.Calls);

        await h.Executor.ExecuteAsync(TestHarness.Command(CommandType.RunScript, new() { ["script"] = "cleanup", ["args"] = "; evil" }), CancellationToken.None);
        Assert.Equal(CommandStatus.Succeeded, h.Updates()[^1].Status);
        var call = Assert.Single(h.Processes.Calls);
        Assert.Equal("/opt/atm/cleanup.sh", call.File);
        Assert.Equal(["--safe"], call.Args);
    }

    [Fact]
    public async Task Failing_script_reports_failed_with_output()
    {
        using var h = new TestHarness(o => o.Scripts["s"] = new ScriptOptions { FileName = "tool" });
        h.Processes.Result = new(2, "disk error", false);
        await h.Executor.ExecuteAsync(TestHarness.Command(CommandType.RunScript, new() { ["script"] = "s" }), CancellationToken.None);
        var last = h.Updates()[^1];
        Assert.Equal(CommandStatus.Failed, last.Status);
        Assert.Contains("disk error", last.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Collect_logs_cannot_read_arbitrary_paths()
    {
        using var h = new TestHarness();
        await h.Executor.ExecuteAsync(TestHarness.Command(CommandType.CollectLogs, new() { ["source"] = "/etc/passwd" }), CancellationToken.None);
        Assert.Equal(CommandStatus.Rejected, h.Updates()[^1].Status);
    }

    [Fact]
    public async Task Set_out_of_service_changes_device_mode()
    {
        using var h = new TestHarness();
        await h.Executor.ExecuteAsync(TestHarness.Command(CommandType.SetOutOfService), CancellationToken.None);
        var snapshot = await h.Devices.GetSnapshotAsync(CancellationToken.None);
        Assert.Equal(OperationalMode.OutOfService, snapshot.Mode);
    }

    [Fact]
    public async Task Reset_device_requires_a_valid_device()
    {
        using var h = new TestHarness();
        await h.Executor.ExecuteAsync(TestHarness.Command(CommandType.ResetDevice, new() { ["device"] = "Toaster" }), CancellationToken.None);
        Assert.Equal(CommandStatus.Rejected, h.Updates()[^1].Status);

        await h.Executor.ExecuteAsync(TestHarness.Command(CommandType.ResetDevice, new() { ["device"] = "cardreader" }), CancellationToken.None);
        Assert.Equal(CommandStatus.Succeeded, h.Updates()[^1].Status);
    }
}
