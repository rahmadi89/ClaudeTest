using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AtmMonitor.Api.Models;
using AtmMonitor.Contracts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace AtmMonitor.Server.Tests.Integration;

/// <summary>Boots the real API against a private in-memory SQLite database. Background jobs are off for determinism.</summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public const string AdminPassword = "Test-Admin-Password-1!";
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private readonly string _connectionString = $"Data Source=file:atm-{Guid.NewGuid():N}?mode=memory&cache=shared";
    private readonly SqliteConnection _keepAlive;

    public ApiFactory()
    {
        // An in-memory SQLite database lives as long as at least one connection is open.
        _keepAlive = new SqliteConnection(_connectionString);
        _keepAlive.Open();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", _connectionString);
        builder.UseSetting("Database:Provider", "Sqlite");
        builder.UseSetting("Jwt:SigningKey", "integration-test-signing-key-0123456789abcdef");
        builder.UseSetting("Bootstrap:AdminPassword", AdminPassword);
        builder.UseSetting("BackgroundJobs:Enabled", "false");
        builder.UseSetting("Monitoring:TelemetrySampleSeconds", "0");
        builder.UseSetting("Serilog:MinimumLevel:Default", "Warning");
        builder.UseSetting("RateLimits:LoginPerMinute", "1000");
        builder.UseSetting("RateLimits:EnrollPerMinute", "1000");
    }

    public async Task<HttpClient> ClientForAsync(string userName, string password)
    {
        var client = CreateClient();
        var res = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(userName, password));
        res.EnsureSuccessStatusCode();
        var login = await res.Content.ReadFromJsonAsync<LoginResponse>(Json);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.AccessToken);
        return client;
    }

    public Task<HttpClient> AdminAsync() => ClientForAsync("admin", AdminPassword);

    public async Task<string> CreateEnrollmentTokenAsync(HttpClient admin, int maxUses = 1)
    {
        var res = await admin.PostAsJsonAsync("/api/admin/enrollment-tokens", new CreateEnrollmentTokenRequest("test", maxUses, 1));
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<CreatedEnrollmentToken>(Json))!.Secret;
    }

    public async Task<EnrollmentResponse> EnrollAsync(string token, string terminalId)
    {
        var res = await CreateClient().PostAsJsonAsync(Protocol.EnrollPath, new EnrollmentRequest(token, terminalId, "test-host", "1.0.0-test", "TestOS"));
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<EnrollmentResponse>(Json))!;
    }

    public HubConnection AgentConnection(EnrollmentResponse creds) => new HubConnectionBuilder()
        .WithUrl(new Uri(Server.BaseAddress, Protocol.AgentHubPath), o =>
        {
            o.HttpMessageHandlerFactory = _ => Server.CreateHandler();
            o.Transports = HttpTransportType.LongPolling;
            o.Headers[Protocol.AgentIdHeader] = creds.AtmId.ToString();
            o.Headers[Protocol.AgentKeyHeader] = creds.AgentKey;
        })
        .AddJsonProtocol(o => o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()))
        .Build();

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _keepAlive.Dispose();
        }
    }

    public T Service<T>() where T : notnull => Services.GetRequiredService<T>();
}
