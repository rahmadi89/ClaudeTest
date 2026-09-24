using AtmMonitor.Api.Auth;
using AtmMonitor.Api.Models;
using AtmMonitor.Api.Services;
using AtmMonitor.Contracts;
using AtmMonitor.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AtmMonitor.Api.Controllers;

[ApiController]
[Route("api/alerts")]
[Authorize(Policy = Policies.Viewer)]
public sealed class AlertsController(AppDbContext db, AuditLogger audit, DashboardNotifier notifier, TimeProvider clock) : ControllerBase
{
    [HttpGet]
    public async Task<PagedResult<AlertView>> List(
        [FromQuery] PageQuery page, [FromQuery] AlertStatus? status, [FromQuery] bool? active, [FromQuery] AlertSeverity? severity,
        [FromQuery] Guid? atmId, CancellationToken ct)
    {
        var q = db.Alerts.AsNoTracking().Include(a => a.Atm).AsQueryable();
        if (status is { } s)
        {
            q = q.Where(a => a.Status == s);
        }

        if (active == true)
        {
            q = q.Where(a => a.Status != AlertStatus.Resolved);
        }

        if (severity is { } sev)
        {
            q = q.Where(a => a.Severity == sev);
        }

        if (atmId is { } id)
        {
            q = q.Where(a => a.AtmId == id);
        }

        var total = await q.CountAsync(ct);
        var items = await q.OrderByDescending(a => a.RaisedAt).Skip(page.Skip).Take(page.PageSize).ToListAsync(ct);
        return new PagedResult<AlertView>(items.Select(a => a.ToView()).ToList(), total, page.Page, page.PageSize);
    }

    [HttpPost("{id:guid}/acknowledge")]
    [Authorize(Policy = Policies.Operator)]
    public Task<ActionResult<AlertView>> Acknowledge(Guid id, AlertActionRequest request, CancellationToken ct) =>
        Act(id, "alert.acknowledge", (a, now) => a.Acknowledge(User.UserName(), request.Note, now), ct);

    [HttpPost("{id:guid}/resolve")]
    [Authorize(Policy = Policies.Operator)]
    public Task<ActionResult<AlertView>> Resolve(Guid id, AlertActionRequest request, CancellationToken ct) =>
        Act(id, "alert.resolve", (a, now) => a.Resolve(User.UserName(), request.Note, now), ct);

    private async Task<ActionResult<AlertView>> Act(Guid id, string action, Func<Domain.Entities.Alert, DateTimeOffset, bool> apply, CancellationToken ct)
    {
        var alert = await db.Alerts.Include(a => a.Atm).FirstOrDefaultAsync(a => a.Id == id, ct);
        if (alert is null)
        {
            return NotFound();
        }

        if (!apply(alert, clock.GetUtcNow()))
        {
            return Conflict(new ProblemDetails { Title = $"Alert is already {alert.Status}.", Status = 409 });
        }

        audit.Record(action, "Alert", alert.Id.ToString(), alert.Message);
        await db.SaveChangesAsync(ct);
        await notifier.AlertsChanged([alert], alert.Atm?.TerminalId ?? "");
        return alert.ToView();
    }
}
