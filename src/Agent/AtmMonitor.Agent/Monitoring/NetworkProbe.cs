using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using AtmMonitor.Agent.Configuration;
using AtmMonitor.Contracts;
using Microsoft.Extensions.Options;

namespace AtmMonitor.Agent.Monitoring;

public interface INetworkProbe
{
    Task<NetworkStatusDto> ProbeAsync(CancellationToken ct);
}

/// <summary>
/// Measures reachability, latency and loss to the transaction host. TCP connect is preferred because bank
/// networks usually block ICMP; the local interface state is read from the OS.
/// </summary>
public sealed class NetworkProbe(IOptions<AgentOptions> options, ILogger<NetworkProbe> logger) : INetworkProbe
{
    public async Task<NetworkStatusDto> ProbeAsync(CancellationToken ct)
    {
        var o = options.Value.HostCheck;
        var nic = SelectInterface(o.InterfaceName);
        var ip = nic?.GetIPProperties().UnicastAddresses
            .FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork)?.Address.ToString();
        var interfaceUp = nic?.OperationalStatus == OperationalStatus.Up;
        // Virtual NICs often report nonsense (e.g. uint.MaxValue); anything above 400 Gbit/s is treated as unknown.
        long? speed = nic is not null && nic.Speed > 0 && nic.Speed / 1_000_000 <= 400_000 ? nic.Speed / 1_000_000 : null;

        if (string.IsNullOrWhiteSpace(o.Host))
        {
            return new NetworkStatusDto(interfaceUp, null, 0, ip, nic?.Name, interfaceUp, speed);
        }

        var latencies = new List<double>();
        for (var i = 0; i < o.Samples; i++)
        {
            var ms = o.Port > 0 ? await TcpProbeAsync(o.Host, o.Port, o.TimeoutMs, ct) : await PingAsync(o.Host, o.TimeoutMs);
            if (ms is { } v)
            {
                latencies.Add(v);
            }
        }

        var loss = 100.0 * (o.Samples - latencies.Count) / o.Samples;
        return new NetworkStatusDto(
            HostReachable: latencies.Count > 0,
            HostLatencyMs: latencies.Count > 0 ? Math.Round(latencies.Average(), 1) : null,
            PacketLossPercent: Math.Round(loss, 1),
            LocalIpAddress: ip,
            InterfaceName: nic?.Name,
            InterfaceUp: interfaceUp,
            LinkSpeedMbps: speed);
    }

    private static NetworkInterface? SelectInterface(string? name)
    {
        var all = NetworkInterface.GetAllNetworkInterfaces();
        if (!string.IsNullOrWhiteSpace(name))
        {
            return all.FirstOrDefault(n => string.Equals(n.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        return all.FirstOrDefault(n => n.OperationalStatus == OperationalStatus.Up &&
                                       n.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                                       n.GetIPProperties().GatewayAddresses.Count > 0)
            ?? all.FirstOrDefault(n => n.NetworkInterfaceType != NetworkInterfaceType.Loopback);
    }

    private async Task<double?> TcpProbeAsync(string host, int port, int timeoutMs, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeoutMs);
        using var client = new TcpClient();
        var sw = Stopwatch.StartNew();
        try
        {
            await client.ConnectAsync(host, port, cts.Token);
            return sw.Elapsed.TotalMilliseconds;
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException && !ct.IsCancellationRequested)
        {
            logger.LogDebug("TCP probe to {Host}:{Port} failed: {Error}", host, port, ex.Message);
            return null;
        }
    }

    private async Task<double?> PingAsync(string host, int timeoutMs)
    {
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(host, timeoutMs);
            return reply.Status == IPStatus.Success ? reply.RoundtripTime : null;
        }
        catch (PingException ex)
        {
            logger.LogDebug("Ping to {Host} failed: {Error}", host, ex.Message);
            return null;
        }
    }
}
