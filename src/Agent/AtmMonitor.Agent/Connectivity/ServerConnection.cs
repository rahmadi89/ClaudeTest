using System.Net;
using System.Text.Json.Serialization;
using AtmMonitor.Agent.Configuration;
using AtmMonitor.Agent.Identity;
using AtmMonitor.Contracts;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Options;

namespace AtmMonitor.Agent.Connectivity;

/// <summary>
/// Owns the persistent SignalR connection to the server: enrollment, authentication, reconnect with jittered
/// exponential backoff, and re-enrollment when the server revokes the key.
/// </summary>
public sealed class ServerConnection(
    IOptions<AgentOptions> options,
    EnrollmentClient enrollment,
    ICredentialStore credentials,
    ILogger<ServerConnection> logger) : IAsyncDisposable
{
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(60);
    private HubConnection? _hub;

    public bool IsConnected => _hub?.State == HubConnectionState.Connected;

    /// <summary>Invoked (sequentially per connection) for every command pushed by the server.</summary>
    public Func<CommandEnvelope, Task>? OnCommand { get; set; }

    /// <summary>Invoked after each successful (re)connect – used to flush the offline buffer and push fresh status.</summary>
    public Func<CancellationToken, Task>? OnConnected { get; set; }

    public async Task<bool> TrySendAsync(string method, object payload, CancellationToken ct)
    {
        var hub = _hub;
        if (hub is not { State: HubConnectionState.Connected })
        {
            return false;
        }

        try
        {
            // InvokeAsync (not SendAsync) so we only treat the message as delivered once the server has processed it.
            await hub.InvokeAsync(method, payload, ct);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning("Sending {Method} failed: {Error}", method, ex.Message);
            return false;
        }
    }

    /// <summary>Connection loop. Returns only when <paramref name="ct"/> is cancelled.</summary>
    public async Task RunAsync(CancellationToken ct)
    {
        var o = options.Value;
        if (!o.AllowInsecureTransport && !o.ServerUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Agent:ServerUrl must use https (set Agent:AllowInsecureTransport only for lab setups).");
        }

        var attempt = 0;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var creds = await enrollment.EnsureEnrolledAsync(ct);
                await using var hub = Build(creds);
                var closed = new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);
                hub.Closed += ex =>
                {
                    closed.TrySetResult(ex);
                    return Task.CompletedTask;
                };

                await hub.StartAsync(ct);
                _hub = hub;
                attempt = 0;
                logger.LogInformation("Connected to {Server} as {TerminalId}", o.ServerUrl, creds.TerminalId);

                if (OnConnected is { } onConnected)
                {
                    _ = Task.Run(() => onConnected(ct), ct);
                }

                var reason = await closed.Task.WaitAsync(ct);
                _hub = null;
                logger.LogWarning("Connection closed: {Reason}", reason?.Message ?? "server closed the connection");
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.Unauthorized)
            {
                _hub = null;
                logger.LogError("Server rejected the agent key (revoked or terminal disabled)");
                if (!string.IsNullOrWhiteSpace(o.EnrollmentToken))
                {
                    logger.LogWarning("Discarding stored credentials and re-enrolling with the configured token");
                    credentials.Delete();
                }
                else
                {
                    await DelayAsync(TimeSpan.FromMinutes(5), ct);
                    continue;
                }
            }
            catch (EnrollmentException ex)
            {
                logger.LogError("Enrollment failed: {Error}", ex.Message);
                await DelayAsync(TimeSpan.FromMinutes(5), ct);
                continue;
            }
            catch (Exception ex)
            {
                _hub = null;
                logger.LogWarning("Connection attempt failed: {Error}", ex.Message);
            }

            await DelayAsync(Backoff(++attempt), ct);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_hub is { } hub)
        {
            await hub.DisposeAsync();
        }
    }

    internal static TimeSpan Backoff(int attempt)
    {
        var baseSeconds = Math.Min(MaxBackoff.TotalSeconds, Math.Pow(2, Math.Min(attempt, 10)));
        // Full jitter avoids thousands of terminals reconnecting in lockstep after a server restart.
        return TimeSpan.FromSeconds(Math.Max(1, Random.Shared.NextDouble() * baseSeconds));
    }

    private HubConnection Build(AgentCredentials creds)
    {
        var hub = new HubConnectionBuilder()
            .WithUrl(new Uri(new Uri(options.Value.ServerUrl), Protocol.AgentHubPath), o =>
            {
                o.Headers[Protocol.AgentIdHeader] = creds.AtmId.ToString();
                o.Headers[Protocol.AgentKeyHeader] = creds.AgentKey;
                o.Headers[Protocol.VersionHeader] = Protocol.Version.ToString(System.Globalization.CultureInfo.InvariantCulture);
            })
            .AddJsonProtocol(o => o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()))
            .Build();

        hub.ServerTimeout = TimeSpan.FromSeconds(60);
        hub.KeepAliveInterval = TimeSpan.FromSeconds(15);
        hub.On<CommandEnvelope>(nameof(IAgentClient.ExecuteCommand), async cmd =>
        {
            if (OnCommand is { } handler)
            {
                await handler(cmd);
            }
        });
        return hub;
    }

    private static async Task DelayAsync(TimeSpan delay, CancellationToken ct)
    {
        try
        {
            await Task.Delay(delay, ct);
        }
        catch (OperationCanceledException)
        {
        }
    }
}
