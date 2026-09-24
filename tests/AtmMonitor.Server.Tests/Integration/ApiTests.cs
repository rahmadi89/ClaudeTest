using System.Net;
using System.Net.Http.Json;
using AtmMonitor.Api.Models;
using AtmMonitor.Contracts;
using AtmMonitor.Domain.Entities;
using Microsoft.AspNetCore.SignalR.Client;

namespace AtmMonitor.Server.Tests.Integration;

public class ApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly System.Text.Json.JsonSerializerOptions Json = ApiFactory.Json;

    [Fact]
    public async Task Health_is_anonymous_but_api_requires_auth()
    {
        var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/ready")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/atms")).StatusCode);
    }

    [Fact]
    public async Task Security_headers_are_set()
    {
        var res = await factory.CreateClient().GetAsync("/health/live");
        Assert.Equal("nosniff", res.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", res.Headers.GetValues("X-Frame-Options").Single());
    }

    [Fact]
    public async Task Repeated_bad_passwords_lock_the_account()
    {
        var admin = await factory.AdminAsync();
        (await admin.PostAsJsonAsync("/api/admin/users", new CreateUserRequest("locky", "Locky", UserRole.Viewer, "Correct-Horse-1!"))).EnsureSuccessStatusCode();

        var client = factory.CreateClient();
        for (var i = 0; i < 5; i++)
        {
            var bad = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("locky", "wrong-password"));
            Assert.Equal(HttpStatusCode.Unauthorized, bad.StatusCode);
        }

        var good = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("locky", "Correct-Horse-1!"));
        Assert.Equal(HttpStatusCode.Unauthorized, good.StatusCode);
    }

    [Fact]
    public async Task Enrollment_rejects_bad_tokens_and_enforces_max_uses()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().PostAsJsonAsync(Protocol.EnrollPath,
            new EnrollmentRequest("not-a-token", "T-BAD", "h", "1", "os"))).StatusCode);

        var admin = await factory.AdminAsync();
        var token = await factory.CreateEnrollmentTokenAsync(admin, maxUses: 1);
        await factory.EnrollAsync(token, "T-ONCE");
        var second = await factory.CreateClient().PostAsJsonAsync(Protocol.EnrollPath, new EnrollmentRequest(token, "T-TWICE", "h", "1", "os"));
        Assert.Equal(HttpStatusCode.Unauthorized, second.StatusCode);
    }

    [Fact]
    public async Task Viewer_cannot_issue_commands_or_use_admin_endpoints()
    {
        var admin = await factory.AdminAsync();
        (await admin.PostAsJsonAsync("/api/admin/users", new CreateUserRequest("viewer1", "Viewer", UserRole.Viewer, "Viewer-Password-1!"))).EnsureSuccessStatusCode();
        var creds = await factory.EnrollAsync(await factory.CreateEnrollmentTokenAsync(admin), "T-VIEW");

        var viewer = await factory.ClientForAsync("viewer1", "Viewer-Password-1!");
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync($"/api/atms/{creds.AtmId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsJsonAsync($"/api/atms/{creds.AtmId}/commands", new CreateCommandRequest { Type = CommandType.Ping })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.GetAsync("/api/admin/users")).StatusCode);
    }

    [Fact]
    public async Task High_impact_command_requires_reason()
    {
        var admin = await factory.AdminAsync();
        var creds = await factory.EnrollAsync(await factory.CreateEnrollmentTokenAsync(admin), "T-REASON");
        var res = await admin.PostAsJsonAsync($"/api/atms/{creds.AtmId}/commands", new CreateCommandRequest { Type = CommandType.RebootMachine });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Agent_round_trip_status_alerts_and_commands()
    {
        var admin = await factory.AdminAsync();
        var creds = await factory.EnrollAsync(await factory.CreateEnrollmentTokenAsync(admin), "T-E2E");

        var received = new TaskCompletionSource<CommandEnvelope>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var agent = factory.AgentConnection(creds);
        agent.On<CommandEnvelope>(nameof(IAgentClient.ExecuteCommand), cmd => received.TrySetResult(cmd));
        await agent.StartAsync();

        // 1. Status report with an empty cassette → terminal degraded + cash alerts.
        await agent.InvokeAsync(AgentHubMethods.ReportStatus, new StatusReport
        {
            CapturedAt = DateTimeOffset.UtcNow,
            AgentVersion = "1.0.0-test",
            Mode = OperationalMode.InService,
            Components = [new(ComponentType.CardReader, ComponentState.Ok)],
            Cassettes = [new("C1", CassetteType.Dispense, "USD", 20, 0, 1000, CassetteStatus.Empty), new("C2", CassetteType.Dispense, "USD", 50, 500, 1000, CassetteStatus.Ok)],
            Network = new(true, 12, 0, "10.1.1.1", "eth0", true, 1000),
            System = new(10, 40, 50, 1_000_000_000, TimeSpan.FromHours(3), "TestOS", "test-host"),
        });

        var detail = await admin.GetFromJsonAsync<AtmDetail>($"/api/atms/{creds.AtmId}", Json);
        Assert.Equal(AtmStatus.Degraded, detail!.Status);
        Assert.True(detail.IsConnected);
        Assert.Equal(25_000m, detail.AvailableCash["USD"]);

        var alerts = await admin.GetFromJsonAsync<PagedResult<AlertView>>($"/api/alerts?atmId={creds.AtmId}&active=true", Json);
        Assert.Contains(alerts!.Items, a => a.Type == AlertType.CashEmpty);

        // 2. Logs are ingested.
        await agent.InvokeAsync(AgentHubMethods.ReportLogs, new LogBatch([new(DateTimeOffset.UtcNow, LogSeverity.Error, "CashDispenser", "Cassette C1 empty")]));
        var logs = await admin.GetFromJsonAsync<PagedResult<LogView>>($"/api/atms/{creds.AtmId}/logs?minSeverity=Warning", Json);
        Assert.Single(logs!.Items);

        // 3. Command is pushed to the connected agent; the agent's result is recorded.
        var issued = await admin.PostAsJsonAsync($"/api/atms/{creds.AtmId}/commands", new CreateCommandRequest { Type = CommandType.Ping });
        Assert.Equal(HttpStatusCode.Accepted, issued.StatusCode);
        var envelope = await received.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(CommandType.Ping, envelope.Type);

        await agent.InvokeAsync(AgentHubMethods.ReportCommandUpdate, new CommandUpdate(envelope.CommandId, CommandStatus.Acknowledged));
        await agent.InvokeAsync(AgentHubMethods.ReportCommandUpdate, new CommandUpdate(envelope.CommandId, CommandStatus.Succeeded, "pong"));
        // A late duplicate must not regress the state.
        await agent.InvokeAsync(AgentHubMethods.ReportCommandUpdate, new CommandUpdate(envelope.CommandId, CommandStatus.Acknowledged));

        var command = await admin.GetFromJsonAsync<CommandView>($"/api/commands/{envelope.CommandId}", Json);
        Assert.Equal(CommandStatus.Succeeded, command!.Status);
        Assert.Equal("pong", command.Output);

        // 4. Replenishment clears the cash alerts automatically.
        await agent.InvokeAsync(AgentHubMethods.ReportStatus, new StatusReport
        {
            CapturedAt = DateTimeOffset.UtcNow.AddSeconds(1),
            AgentVersion = "1.0.0-test",
            Mode = OperationalMode.InService,
            Components = [new(ComponentType.CardReader, ComponentState.Ok)],
            Cassettes = [new("C1", CassetteType.Dispense, "USD", 20, 1000, 1000, CassetteStatus.Ok), new("C2", CassetteType.Dispense, "USD", 50, 500, 1000, CassetteStatus.Ok)],
            Network = new(true, 12, 0, "10.1.1.1", "eth0", true, 1000),
        });
        alerts = await admin.GetFromJsonAsync<PagedResult<AlertView>>($"/api/alerts?atmId={creds.AtmId}&active=true", Json);
        Assert.Empty(alerts!.Items);
        detail = await admin.GetFromJsonAsync<AtmDetail>($"/api/atms/{creds.AtmId}", Json);
        Assert.Equal(AtmStatus.Online, detail!.Status);
    }

    [Fact]
    public async Task Commands_issued_while_offline_are_delivered_on_connect()
    {
        var admin = await factory.AdminAsync();
        var creds = await factory.EnrollAsync(await factory.CreateEnrollmentTokenAsync(admin), "T-QUEUE");
        var issued = await admin.PostAsJsonAsync($"/api/atms/{creds.AtmId}/commands", new CreateCommandRequest { Type = CommandType.RefreshStatus });
        var queued = await issued.Content.ReadFromJsonAsync<CommandView>(Json);
        Assert.Equal(CommandStatus.Pending, queued!.Status);

        var received = new TaskCompletionSource<CommandEnvelope>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var agent = factory.AgentConnection(creds);
        agent.On<CommandEnvelope>(nameof(IAgentClient.ExecuteCommand), cmd => received.TrySetResult(cmd));
        await agent.StartAsync();

        var envelope = await received.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(queued.Id, envelope.CommandId);
    }

    [Fact]
    public async Task Revoked_agent_key_is_rejected()
    {
        var admin = await factory.AdminAsync();
        var creds = await factory.EnrollAsync(await factory.CreateEnrollmentTokenAsync(admin), "T-REVOKE");
        (await admin.PostAsync($"/api/atms/{creds.AtmId}/revoke-agent", null)).EnsureSuccessStatusCode();

        await using var agent = factory.AgentConnection(creds);
        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => agent.StartAsync());
        Assert.Equal(HttpStatusCode.Unauthorized, ex.StatusCode);
    }

    [Fact]
    public async Task Audit_trail_records_command_issuance()
    {
        var admin = await factory.AdminAsync();
        var creds = await factory.EnrollAsync(await factory.CreateEnrollmentTokenAsync(admin), "T-AUDIT");
        await admin.PostAsJsonAsync($"/api/atms/{creds.AtmId}/commands", new CreateCommandRequest { Type = CommandType.SetOutOfService, Reason = "replenishment" });

        var audit = await admin.GetFromJsonAsync<PagedResult<AuditView>>("/api/admin/audit?action=command.create", Json);
        Assert.Contains(audit!.Items, a => a.TargetId == "T-AUDIT" && a.Details!.Contains("replenishment", StringComparison.Ordinal));
    }
}
