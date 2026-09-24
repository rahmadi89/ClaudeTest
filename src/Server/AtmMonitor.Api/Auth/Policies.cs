using AtmMonitor.Domain.Entities;
using Microsoft.AspNetCore.Authorization;

namespace AtmMonitor.Api.Auth;

public static class Policies
{
    public const string Viewer = nameof(Viewer);
    public const string Operator = nameof(Operator);
    public const string Admin = nameof(Admin);
    public const string Agent = nameof(Agent);

    public const string AgentRole = "Agent";
    public const string TerminalIdClaim = "terminal_id";

    public static void Register(AuthorizationOptions o)
    {
        // Roles are hierarchical: Admin ⊃ Operator ⊃ Viewer.
        o.AddPolicy(Viewer, p => p.RequireAuthenticatedUser().AddAuthenticationSchemes("Bearer").RequireRole(RolesAtLeast(UserRole.Viewer)));
        o.AddPolicy(Operator, p => p.RequireAuthenticatedUser().AddAuthenticationSchemes("Bearer").RequireRole(RolesAtLeast(UserRole.Operator)));
        o.AddPolicy(Admin, p => p.RequireAuthenticatedUser().AddAuthenticationSchemes("Bearer").RequireRole(RolesAtLeast(UserRole.Admin)));
        o.AddPolicy(Agent, p => p.RequireAuthenticatedUser().AddAuthenticationSchemes(AgentKeyAuthenticationHandler.SchemeName).RequireRole(AgentRole));
        o.DefaultPolicy = o.GetPolicy(Viewer)!;
        o.FallbackPolicy = o.GetPolicy(Viewer);
    }

    private static string[] RolesAtLeast(UserRole min) =>
        Enum.GetValues<UserRole>().Where(r => r >= min).Select(r => r.ToString()).ToArray();
}
