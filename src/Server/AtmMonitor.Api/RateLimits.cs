using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace AtmMonitor.Api;

public static class RateLimits
{
    public const string Login = "login";
    public const string Enroll = "enroll";

    /// <summary>Per-client-IP fixed windows. Limits are configurable under <c>RateLimits:*PerMinute</c>.</summary>
    public static void Configure(RateLimiterOptions o, IConfiguration config)
    {
        var login = config.GetValue("RateLimits:LoginPerMinute", 10);
        var enroll = config.GetValue("RateLimits:EnrollPerMinute", 30);
        o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        o.AddPolicy(Login, ctx => PerIp(ctx, login));
        o.AddPolicy(Enroll, ctx => PerIp(ctx, enroll));
    }

    private static RateLimitPartition<string> PerIp(HttpContext ctx, int permitsPerMinute) =>
        RateLimitPartition.GetFixedWindowLimiter(
            ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = permitsPerMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 });
}
