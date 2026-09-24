using AtmMonitor.Contracts;

namespace AtmMonitor.Domain.Entities;

public class AtmCommand
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AtmId { get; set; }
    public Atm? Atm { get; set; }
    public CommandType Type { get; set; }
    public Dictionary<string, string> Parameters { get; set; } = [];
    public CommandStatus Status { get; set; } = CommandStatus.Pending;
    public required string RequestedBy { get; set; }
    public string? Reason { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? SentAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int DeliveryAttempts { get; set; }
    public string? Output { get; set; }
    public string? Error { get; set; }

    /// <summary>Concurrency token so two server instances cannot race a transition.</summary>
    public Guid Version { get; set; } = Guid.NewGuid();

    public const int MaxOutputLength = 64 * 1024;

    private static readonly Dictionary<CommandStatus, CommandStatus[]> AllowedTransitions = new()
    {
        [CommandStatus.Pending] = [CommandStatus.Sent, CommandStatus.Cancelled, CommandStatus.TimedOut, CommandStatus.Rejected,
                                   // Agents may answer before the server records "Sent" (race on fast commands).
                                   CommandStatus.Acknowledged, CommandStatus.Running, CommandStatus.Succeeded, CommandStatus.Failed],
        [CommandStatus.Sent] = [CommandStatus.Acknowledged, CommandStatus.Running, CommandStatus.Succeeded, CommandStatus.Failed,
                                CommandStatus.Rejected, CommandStatus.TimedOut, CommandStatus.Cancelled, CommandStatus.Pending],
        // Rejected after Acknowledged/Running: the agent accepted delivery but local policy refused execution.
        [CommandStatus.Acknowledged] = [CommandStatus.Running, CommandStatus.Succeeded, CommandStatus.Failed, CommandStatus.Rejected, CommandStatus.TimedOut],
        [CommandStatus.Running] = [CommandStatus.Succeeded, CommandStatus.Failed, CommandStatus.Rejected, CommandStatus.TimedOut],
    };

    public static bool CanTransition(CommandStatus from, CommandStatus to) =>
        AllowedTransitions.TryGetValue(from, out var allowed) && allowed.Contains(to);

    /// <summary>
    /// Moves the command to <paramref name="to"/> if the transition is legal.
    /// Duplicate or out-of-order updates (common with at-least-once delivery) are ignored and return <c>false</c>.
    /// </summary>
    public bool TryTransition(CommandStatus to, DateTimeOffset now, string? output = null, string? error = null)
    {
        if (!CanTransition(Status, to))
        {
            return false;
        }

        Status = to;
        UpdatedAt = now;
        Version = Guid.NewGuid();
        if (to == CommandStatus.Sent)
        {
            SentAt = now;
            DeliveryAttempts++;
        }

        if (output is not null)
        {
            Output = Truncate(output);
        }

        if (error is not null)
        {
            Error = Truncate(error);
        }

        if (to.IsTerminal())
        {
            CompletedAt = now;
        }

        return true;
    }

    public CommandEnvelope ToEnvelope() => new(Id, Type, Parameters, CreatedAt, ExpiresAt);

    private static string Truncate(string value) =>
        value.Length <= MaxOutputLength ? value : value[..MaxOutputLength] + "\n…[truncated]";
}
