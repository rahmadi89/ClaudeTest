using System.Text.Json;
using System.Text.Json.Serialization;
using AtmMonitor.Agent.Storage;
using AtmMonitor.Contracts;

namespace AtmMonitor.Agent.Connectivity;

/// <summary>
/// Store-and-forward delivery. Logs and command results are durable (buffered on disk while offline, delivered in order);
/// status reports are snapshots and are simply dropped while offline – a fresh one is sent on reconnect.
/// </summary>
public sealed class Messenger(ServerConnection connection, LocalStore store, ILogger<Messenger> logger) : IDisposable
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private readonly SemaphoreSlim _flushGate = new(1, 1);

    public Task<bool> SendStatusAsync(StatusReport report, CancellationToken ct) =>
        connection.TrySendAsync(AgentHubMethods.ReportStatus, report, ct);

    public Task SendLogsAsync(LogBatch batch, CancellationToken ct) =>
        SendDurableAsync(AgentHubMethods.ReportLogs, JsonSerializer.Serialize(batch, Json), ct);

    public Task SendCommandUpdateAsync(CommandUpdate update, CancellationToken ct) =>
        SendDurableAsync(AgentHubMethods.ReportCommandUpdate, JsonSerializer.Serialize(update, Json), ct);

    /// <summary>Delivers buffered messages in FIFO order; stops at the first failure to preserve ordering.</summary>
    public async Task FlushAsync(CancellationToken ct)
    {
        await _flushGate.WaitAsync(ct);
        try
        {
            while (connection.IsConnected && !ct.IsCancellationRequested)
            {
                var batch = store.Peek(100);
                if (batch.Count == 0)
                {
                    return;
                }

                foreach (var item in batch)
                {
                    object? payload = item.Kind switch
                    {
                        AgentHubMethods.ReportLogs => JsonSerializer.Deserialize<LogBatch>(item.Payload, Json),
                        AgentHubMethods.ReportCommandUpdate => JsonSerializer.Deserialize<CommandUpdate>(item.Payload, Json),
                        _ => null,
                    };

                    if (payload is null)
                    {
                        logger.LogWarning("Dropping unreadable outbox item {Id} ({Kind})", item.Id, item.Kind);
                        store.Remove(item.Id);
                        continue;
                    }

                    if (!await connection.TrySendAsync(item.Kind, payload, ct))
                    {
                        return;
                    }

                    store.Remove(item.Id);
                }
            }
        }
        finally
        {
            _flushGate.Release();
        }
    }

    public void Dispose() => _flushGate.Dispose();

    private async Task SendDurableAsync(string method, string json, CancellationToken ct)
    {
        // Always go through the outbox so that a crash between "send" and "ack" can never lose the message,
        // and so that ordering with earlier buffered items is preserved.
        store.Enqueue(method, json);
        await FlushAsync(ct);
    }
}
