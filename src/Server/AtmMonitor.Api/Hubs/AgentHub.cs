using AtmMonitor.Api.Auth;
using AtmMonitor.Api.Services;
using AtmMonitor.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace AtmMonitor.Api.Hubs;

[Authorize(Policy = Policies.Agent)]
public sealed class AgentHub(
    AgentConnectionRegistry registry,
    StatusIngestionService ingestion,
    CommandService commands,
    ILogger<AgentHub> logger) : Hub<IAgentClient>
{
    public static string GroupFor(Guid atmId) => $"atm:{atmId:N}";

    private Guid AtmId => Context.User!.AtmId();

    public override async Task OnConnectedAsync()
    {
        var atmId = AtmId;
        registry.Register(atmId, Context);
        await Groups.AddToGroupAsync(Context.ConnectionId, GroupFor(atmId));
        logger.LogInformation("Agent connected for {AtmId} ({ConnectionId})", atmId, Context.ConnectionId);
        await ingestion.MarkConnectedAsync(atmId, Context.ConnectionAborted);
        await commands.DeliverOutstandingAsync(atmId, Context.ConnectionAborted);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var atmId = AtmId;
        if (registry.Unregister(atmId, Context.ConnectionId))
        {
            await ingestion.MarkDisconnectedAsync(atmId, CancellationToken.None);
        }

        logger.LogInformation(exception, "Agent disconnected for {AtmId} ({ConnectionId})", atmId, Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }

    public Task ReportStatus(StatusReport report) =>
        ingestion.IngestStatusAsync(AtmId, report, Context.ConnectionAborted);

    public Task ReportLogs(LogBatch batch) =>
        ingestion.IngestLogsAsync(AtmId, batch, Context.ConnectionAborted);

    public Task ReportCommandUpdate(CommandUpdate update) =>
        commands.ApplyAgentUpdateAsync(AtmId, update, Context.ConnectionAborted);
}
