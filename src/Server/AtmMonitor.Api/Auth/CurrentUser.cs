using System.Security.Claims;
using AtmMonitor.Domain.Entities;

namespace AtmMonitor.Api.Auth;

public static class ClaimsPrincipalExtensions
{
    public static string UserName(this ClaimsPrincipal user) => user.Identity?.Name ?? "anonymous";

    public static Guid UserId(this ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : Guid.Empty;

    public static UserRole Role(this ClaimsPrincipal user) =>
        Enum.TryParse<UserRole>(user.FindFirstValue(ClaimTypes.Role), out var r) ? r : UserRole.Viewer;

    /// <summary>For agent connections: the ATM the agent key belongs to.</summary>
    public static Guid AtmId(this ClaimsPrincipal user) => user.UserId();
}
