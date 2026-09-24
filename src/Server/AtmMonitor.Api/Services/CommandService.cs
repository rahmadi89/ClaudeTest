using AtmMonitor.Api.Hubs;
using AtmMonitor.Contracts;
using AtmMonitor.Domain.Entities;
using AtmMonitor.Domain.Services;
using AtmMonitor.Infrastructure.Alerts;
using AtmMonitor.Infrastructure.Persistence;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace AtmMonitor.Api.Services;

public sealed class CommandValidationException(string message) : Exception(message);

/// <summary>
/// Command lifecycle: Pending → Sent → (Acknowledged → Running →) Succeeded | Failed | TimedOut, or Cancelled/Rejected.
/// Delivery is at-least-once: undelivered/unacknowledged commands are re-sent when the agent reconnects, and the
/// agent de-duplicates by CommandId.
/// </summary>
public sealed class CommandService(
    AppDbContext db,
    IHubContext<AgentHub, IAgentClient> agentHub,
    AlertManager alerts,
    DashboardNotifier notifier,
    AuditLogger audit,
    TimeProvider clock,
    ILogger<CommandService> logger)
{
    public const int MaxParameters = 16;
    public const int MaxParameterLength = 512;

    public async Task<AtmCommand> CreateAsync(
        Guid atmId, CommandType type, IReadOnlyDictionary<string, string>? parameters, string? reason,
        string requestedBy, UserRole role, CancellationToken ct)
    {
        if (!Enum.IsDefined(type))
        {
            throw new CommandValidationException("Unknown command type.");
        }

        if (!CommandPolicy.CanIssue(role, type))
        {
            throw new UnauthorizedAccessException($"Role {role} may not issue {type}.");
        }

        if (CommandPolicy.RequiresReason(type) && string.IsNullOrWhiteSpace(reason))
        {
            throw new CommandValidationException($"{type} requires a reason.");
        }

        parameters ??= new Dictionary<string, string>();
        if (parameters.Count > MaxParameters || parameters.Any(p => p.Key.Length > 64 || p.Value.Length > MaxParameterLength))
        {
            throw new CommandValidationException("Command parameters exceed allowed size.");
        }

        if (type == CommandType.RunScript && !parameters.ContainsKey("script"))
        {
            throw new CommandValidationException("RunScript requires a 'script' parameter naming an allow-listed script.");
        }

        if (type == CommandType.ResetDevice && !parameters.ContainsKey("device"))
        {
            throw new CommandValidationException("ResetDevice requires a 'device' parameter.");
        }

        var atm = await db.Atms.FirstOrDefaultAsync(a => a.Id == atmId, ct)
            ?? throw new KeyNotFoundException("ATM not found.");
        if (!atm.IsEnabled || atm.AgentKeyHash is null)
        {
            throw new CommandValidationException("ATM is disabled or has no enrolled agent.");
        }

        var now = clock.GetUtcNow();
        var command = new AtmCommand
        {
            AtmId = atmId,
            Type = type,
            Parameters = new Dictionary<string, string>(parameters),
            RequestedBy = requestedBy,
            Reason = reason?.Trim(),
            CreatedAt = now,
            UpdatedAt = now,
            ExpiresAt = now + CommandPolicy.DefaultTimeout(type),
        };
        db.Commands.Add(command);
        audit.Record("command.create", "Atm", atm.TerminalId, $"{type} ({command.Id}) reason: {reason}");
        await db.SaveChangesAsync(ct);

        await notifier.CommandChanged(command);
        if (atm.IsConnected)
        {
            await SendAsync(command, ct);
        }

        return command;
    }

    /// <summary>Re-sends everything the agent has not acknowledged yet. Called when an agent (re)connects.</summary>
    public async Task DeliverOutstandingAsync(Guid atmId, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var outstanding = await db.Commands
            .Where(c => c.AtmId == atmId && (c.Status == CommandStatus.Pending || c.Status == CommandStatus.Sent))
            .OrderBy(c => c.CreatedAt)
            .ToListAsync(ct);

        foreach (var c in outstanding.Where(c => c.ExpiresAt > now))
        {
            await SendAsync(c, ct);
        }
    }

    public async Task ApplyAgentUpdateAsync(Guid atmId, CommandUpdate update, CancellationToken ct)
    {
        var command = await db.Commands.FirstOrDefaultAsync(c => c.Id == update.CommandId && c.AtmId == atmId, ct);
        if (command is null)
        {
            logger.LogWarning("Agent {AtmId} reported update for unknown command {CommandId}", atmId, update.CommandId);
            return;
        }

        var now = clock.GetUtcNow();
        if (!command.TryTransition(update.Status, now, update.Output, update.Error))
        {
            logger.LogDebug("Ignoring {From} -> {To} for command {CommandId}", command.Status, update.Status, command.Id);
            return;
        }

        var terminalId = "";
        Alert? failure = null;
        if (update.Status is CommandStatus.Failed or CommandStatus.Rejected)
        {
            terminalId = await db.Atms.Where(a => a.Id == atmId).Select(a => a.TerminalId).FirstAsync(ct);
            failure = alerts.RaiseEvent(atmId, AlertType.CommandFailed, AlertSeverity.Warning,
                $"{command.Type} {update.Status.ToString().ToLowerInvariant()} on {terminalId}: {update.Error ?? "no details"}");
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Another instance (or the timeout worker) moved it first; the agent will not re-send stale updates.
            logger.LogInformation("Concurrent update on command {CommandId}; dropping {Status}", command.Id, update.Status);
            return;
        }

        await notifier.CommandChanged(command);
        if (failure is not null)
        {
            await notifier.AlertsChanged([failure], terminalId);
        }
    }

    public async Task<AtmCommand> CancelAsync(Guid commandId, CancellationToken ct)
    {
        var command = await db.Commands.FirstOrDefaultAsync(c => c.Id == commandId, ct)
            ?? throw new KeyNotFoundException("Command not found.");
        if (command.Status is not (CommandStatus.Pending or CommandStatus.Sent) ||
            !command.TryTransition(CommandStatus.Cancelled, clock.GetUtcNow()))
        {
            throw new CommandValidationException($"Command in state {command.Status} cannot be cancelled.");
        }

        audit.Record("command.cancel", "Command", command.Id.ToString());
        await db.SaveChangesAsync(ct);
        await notifier.CommandChanged(command);
        return command;
    }

    private async Task SendAsync(AtmCommand command, CancellationToken ct)
    {
        try
        {
            await agentHub.Clients.Group(AgentHub.GroupFor(command.AtmId)).ExecuteCommand(command.ToEnvelope());
            if (command.Status == CommandStatus.Pending || command.Status == CommandStatus.Sent)
            {
                if (command.Status == CommandStatus.Sent)
                {
                    command.DeliveryAttempts++;
                }
                else
                {
                    command.TryTransition(CommandStatus.Sent, clock.GetUtcNow());
                }

                await db.SaveChangesAsync(ct);
                await notifier.CommandChanged(command);
            }
        }
        catch (DbUpdateConcurrencyException)
        {
            // The agent answered before we persisted "Sent" – its update wins.
            await db.Entry(command).ReloadAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to send command {CommandId} to {AtmId}; will retry on reconnect", command.Id, command.AtmId);
        }
    }
}
