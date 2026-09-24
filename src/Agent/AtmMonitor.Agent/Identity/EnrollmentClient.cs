using System.Net;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using AtmMonitor.Agent.Configuration;
using AtmMonitor.Contracts;
using Microsoft.Extensions.Options;

namespace AtmMonitor.Agent.Identity;

public sealed class EnrollmentException(string message) : Exception(message);

public sealed class EnrollmentClient(HttpClient http, IOptions<AgentOptions> options, ICredentialStore store, ILogger<EnrollmentClient> logger)
{
    /// <summary>Returns stored credentials, enrolling first if there are none (or they belong to another terminal id).</summary>
    public async Task<AgentCredentials> EnsureEnrolledAsync(CancellationToken ct)
    {
        var o = options.Value;
        var existing = store.Load();
        if (existing is not null && existing.TerminalId == o.TerminalId)
        {
            return existing;
        }

        return await EnrollAsync(ct);
    }

    public async Task<AgentCredentials> EnrollAsync(CancellationToken ct)
    {
        var o = options.Value;
        if (string.IsNullOrWhiteSpace(o.EnrollmentToken))
        {
            throw new EnrollmentException("Agent is not enrolled and no Agent:EnrollmentToken is configured.");
        }

        logger.LogInformation("Enrolling terminal {TerminalId} with {Server}", o.TerminalId, o.ServerUrl);
        var request = new EnrollmentRequest(o.EnrollmentToken, o.TerminalId, Environment.MachineName, AgentInfo.Version, RuntimeInformation.OSDescription);
        using var response = await http.PostAsJsonAsync(new Uri(new Uri(o.ServerUrl), Protocol.EnrollPath), request, ct);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            throw new EnrollmentException($"Enrollment rejected by server ({(int)response.StatusCode}). Check the enrollment token.");
        }

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<EnrollmentResponse>(ct)
            ?? throw new EnrollmentException("Empty enrollment response.");

        var credentials = new AgentCredentials(result.AtmId, result.AgentKey, o.TerminalId);
        store.Save(credentials);
        logger.LogInformation("Enrolled as {AtmId}. The enrollment token can now be removed from configuration.", result.AtmId);
        return credentials;
    }
}
