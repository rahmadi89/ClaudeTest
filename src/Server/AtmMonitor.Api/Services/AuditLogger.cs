using AtmMonitor.Api.Auth;
using AtmMonitor.Domain.Entities;
using AtmMonitor.Infrastructure.Persistence;

namespace AtmMonitor.Api.Services;

/// <summary>Stages an audit record on the current unit of work. It is committed with the change it describes.</summary>
public sealed class AuditLogger(AppDbContext db, IHttpContextAccessor http, TimeProvider clock)
{
    public void Record(string action, string? targetType = null, string? targetId = null, string? details = null, string? actor = null)
    {
        var ctx = http.HttpContext;
        db.Audit.Add(new AuditEntry
        {
            Timestamp = clock.GetUtcNow(),
            Actor = actor ?? ctx?.User.UserName() ?? "system",
            Action = action,
            TargetType = targetType,
            TargetId = targetId,
            Details = details is { Length: > 2000 } ? details[..2000] : details,
            IpAddress = ctx?.Connection.RemoteIpAddress?.ToString(),
        });
    }
}
