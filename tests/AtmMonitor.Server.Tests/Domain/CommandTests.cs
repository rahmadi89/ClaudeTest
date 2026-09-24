using AtmMonitor.Contracts;
using AtmMonitor.Domain.Entities;
using AtmMonitor.Domain.Services;

namespace AtmMonitor.Server.Tests.Domain;

public class CommandTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private static AtmCommand New() => new() { RequestedBy = "op", CreatedAt = T0, ExpiresAt = T0.AddMinutes(5) };

    [Fact]
    public void Happy_path_transitions()
    {
        var c = New();
        Assert.True(c.TryTransition(CommandStatus.Sent, T0));
        Assert.True(c.TryTransition(CommandStatus.Acknowledged, T0));
        Assert.True(c.TryTransition(CommandStatus.Running, T0));
        Assert.True(c.TryTransition(CommandStatus.Succeeded, T0.AddSeconds(3), output: "ok"));
        Assert.Equal(T0.AddSeconds(3), c.CompletedAt);
        Assert.Equal(1, c.DeliveryAttempts);
    }

    [Fact]
    public void Terminal_states_are_final_and_duplicates_are_ignored()
    {
        var c = New();
        c.TryTransition(CommandStatus.Succeeded, T0);
        Assert.False(c.TryTransition(CommandStatus.Failed, T0));
        Assert.False(c.TryTransition(CommandStatus.Succeeded, T0));
        Assert.Equal(CommandStatus.Succeeded, c.Status);
    }

    [Fact]
    public void Agent_may_reject_after_acknowledging()
    {
        var c = New();
        c.TryTransition(CommandStatus.Sent, T0);
        c.TryTransition(CommandStatus.Acknowledged, T0);
        c.TryTransition(CommandStatus.Running, T0);
        Assert.True(c.TryTransition(CommandStatus.Rejected, T0, error: "disabled"));
    }

    [Fact]
    public void Running_command_cannot_be_cancelled()
    {
        var c = New();
        c.TryTransition(CommandStatus.Running, T0);
        Assert.False(c.TryTransition(CommandStatus.Cancelled, T0));
    }

    [Fact]
    public void Output_is_truncated()
    {
        var c = New();
        c.TryTransition(CommandStatus.Succeeded, T0, output: new string('x', AtmCommand.MaxOutputLength + 100));
        Assert.True(c.Output!.Length < AtmCommand.MaxOutputLength + 50);
    }

    [Theory]
    [InlineData(UserRole.Viewer, CommandType.Ping, false)]
    [InlineData(UserRole.Operator, CommandType.Ping, true)]
    [InlineData(UserRole.Operator, CommandType.RebootMachine, false)]
    [InlineData(UserRole.Admin, CommandType.RebootMachine, true)]
    [InlineData(UserRole.Operator, CommandType.RunScript, false)]
    public void Command_policy_by_role(UserRole role, CommandType type, bool allowed) =>
        Assert.Equal(allowed, CommandPolicy.CanIssue(role, type));
}
