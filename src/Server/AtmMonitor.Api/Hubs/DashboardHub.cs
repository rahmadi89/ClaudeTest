using AtmMonitor.Api.Auth;
using AtmMonitor.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace AtmMonitor.Api.Hubs;

/// <summary>Push-only hub for the web console. Browsers authenticate with the JWT via the access_token query string.</summary>
[Authorize(Policy = Policies.Viewer)]
public sealed class DashboardHub : Hub<IDashboardClient>;
