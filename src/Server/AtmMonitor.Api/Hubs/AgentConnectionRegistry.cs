using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;

namespace AtmMonitor.Api.Hubs;

/// <summary>
/// Tracks agent connections held by <em>this</em> server instance so they can be forcibly closed
/// (key revoked, terminal disabled) and so a newer connection from the same terminal supersedes an older one.
/// </summary>
public sealed class AgentConnectionRegistry
{
    private readonly ConcurrentDictionary<Guid, HubCallerContext> _connections = new();

    /// <summary>Registers the connection and aborts any previous connection for the same terminal.</summary>
    public void Register(Guid atmId, HubCallerContext context)
    {
        HubCallerContext? displaced = null;
        _connections.AddOrUpdate(atmId, context, (_, old) =>
        {
            displaced = old;
            return context;
        });
        if (displaced is not null && displaced.ConnectionId != context.ConnectionId)
        {
            displaced.Abort();
        }
    }

    public bool Unregister(Guid atmId, string connectionId) =>
        _connections.TryGetValue(atmId, out var ctx) && ctx.ConnectionId == connectionId &&
        _connections.TryRemove(new KeyValuePair<Guid, HubCallerContext>(atmId, ctx));

    public bool IsConnectedHere(Guid atmId) => _connections.ContainsKey(atmId);

    public void Disconnect(Guid atmId)
    {
        if (_connections.TryRemove(atmId, out var ctx))
        {
            ctx.Abort();
        }
    }

    public int Count => _connections.Count;
}
