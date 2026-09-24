using AtmMonitor.Api.Auth;
using AtmMonitor.Api.Hubs;
using AtmMonitor.Api.Models;
using AtmMonitor.Api.Services;
using AtmMonitor.Contracts;
using AtmMonitor.Domain.Entities;
using AtmMonitor.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AtmMonitor.Api.Controllers;

[ApiController]
[Route("api/atms")]
[Authorize(Policy = Policies.Viewer)]
public sealed class AtmsController(
    AppDbContext db, AuditLogger audit, AgentConnectionRegistry agents, CommandService commands, TimeProvider clock) : ControllerBase
{
    [HttpGet]
    public async Task<PagedResult<AtmListItem>> List(
        [FromQuery] PageQuery page, [FromQuery] AtmStatus? status, [FromQuery] string? search, [FromQuery] string? city, CancellationToken ct)
    {
        var q = db.Atms.AsNoTracking();
        if (status is { } s)
        {
            q = q.Where(a => a.Status == s);
        }

        if (!string.IsNullOrWhiteSpace(city))
        {
            q = q.Where(a => a.City == city);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            q = q.Where(a => a.TerminalId.ToLower().Contains(term) || a.Name.ToLower().Contains(term) ||
                             (a.Branch != null && a.Branch.ToLower().Contains(term)));
        }

        var total = await q.CountAsync(ct);
        var atms = await q.OrderBy(a => a.TerminalId).Skip(page.Skip).Take(page.PageSize)
            .Include(a => a.Cassettes).AsSplitQuery().ToListAsync(ct);

        var ids = atms.Select(a => a.Id).ToList();
        var openAlerts = await db.Alerts.AsNoTracking()
            .Where(a => ids.Contains(a.AtmId) && a.Status != AlertStatus.Resolved)
            .GroupBy(a => a.AtmId).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);

        var items = atms.Select(a => new AtmListItem(
            a.Id, a.TerminalId, a.Name, a.Branch, a.City, a.Status, a.Mode, a.IsEnabled, a.AgentKeyHash != null, a.IsConnected,
            a.LastSeenAt, a.AgentVersion, openAlerts.GetValueOrDefault(a.Id), a.AvailableCash().Values.Sum(),
            a.Cassettes.Count(c => c.IsLowOrEmpty()))).ToList();

        return new PagedResult<AtmListItem>(items, total, page.Page, page.PageSize);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<AtmDetail>> Get(Guid id, CancellationToken ct)
    {
        var atm = await db.Atms.AsNoTracking().Include(a => a.Components).Include(a => a.Cassettes).AsSplitQuery()
            .FirstOrDefaultAsync(a => a.Id == id, ct);
        return atm is null ? NotFound() : atm.ToDetail();
    }

    [HttpPost]
    [Authorize(Policy = Policies.Admin)]
    public async Task<ActionResult<AtmDetail>> Create(UpsertAtmRequest request, CancellationToken ct)
    {
        if (await db.Atms.AnyAsync(a => a.TerminalId == request.TerminalId, ct))
        {
            return Conflict(new ProblemDetails { Title = $"Terminal {request.TerminalId} already exists.", Status = 409 });
        }

        var atm = new Atm { TerminalId = request.TerminalId, Name = request.Name, CreatedAt = clock.GetUtcNow() };
        Apply(atm, request);
        db.Atms.Add(atm);
        audit.Record("atm.create", "Atm", atm.TerminalId);
        await db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(Get), new { id = atm.Id }, atm.ToDetail());
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = Policies.Admin)]
    public async Task<ActionResult<AtmDetail>> Update(Guid id, UpsertAtmRequest request, CancellationToken ct)
    {
        var atm = await db.Atms.Include(a => a.Components).Include(a => a.Cassettes).AsSplitQuery().FirstOrDefaultAsync(a => a.Id == id, ct);
        if (atm is null)
        {
            return NotFound();
        }

        if (atm.TerminalId != request.TerminalId && await db.Atms.AnyAsync(a => a.TerminalId == request.TerminalId, ct))
        {
            return Conflict(new ProblemDetails { Title = $"Terminal {request.TerminalId} already exists.", Status = 409 });
        }

        atm.TerminalId = request.TerminalId;
        Apply(atm, request);
        audit.Record("atm.update", "Atm", atm.TerminalId);
        await db.SaveChangesAsync(ct);
        return atm.ToDetail();
    }

    [HttpPost("{id:guid}/enable")]
    [Authorize(Policy = Policies.Admin)]
    public Task<IActionResult> Enable(Guid id, CancellationToken ct) => SetEnabled(id, true, ct);

    [HttpPost("{id:guid}/disable")]
    [Authorize(Policy = Policies.Admin)]
    public Task<IActionResult> Disable(Guid id, CancellationToken ct) => SetEnabled(id, false, ct);

    /// <summary>Invalidates the agent key (e.g. suspected compromise). The terminal must re-enroll.</summary>
    [HttpPost("{id:guid}/revoke-agent")]
    [Authorize(Policy = Policies.Admin)]
    public async Task<IActionResult> RevokeAgent(Guid id, CancellationToken ct)
    {
        var atm = await db.Atms.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (atm is null)
        {
            return NotFound();
        }

        atm.AgentKeyHash = null;
        atm.IsConnected = false;
        audit.Record("atm.agent.revoke", "Atm", atm.TerminalId);
        await db.SaveChangesAsync(ct);
        agents.Disconnect(id);
        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = Policies.Admin)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var atm = await db.Atms.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (atm is null)
        {
            return NotFound();
        }

        db.Atms.Remove(atm);
        audit.Record("atm.delete", "Atm", atm.TerminalId);
        await db.SaveChangesAsync(ct);
        agents.Disconnect(id);
        return NoContent();
    }

    [HttpGet("{id:guid}/telemetry")]
    public async Task<ActionResult<IReadOnlyList<TelemetryPoint>>> Telemetry(Guid id, [FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to, CancellationToken ct)
    {
        var end = to ?? clock.GetUtcNow();
        var start = from ?? end.AddHours(-24);
        if (end - start > TimeSpan.FromDays(31) || start >= end)
        {
            return ValidationProblem("Range must be positive and at most 31 days.");
        }

        return await db.Telemetry.AsNoTracking()
            .Where(t => t.AtmId == id && t.Timestamp >= start && t.Timestamp <= end)
            .OrderBy(t => t.Timestamp)
            .Take(5000)
            .Select(t => new TelemetryPoint(t.Timestamp, t.CpuPercent, t.MemoryUsedPercent, t.DiskUsedPercent, t.HostLatencyMs, t.PacketLossPercent, t.HostReachable, t.AvailableCash))
            .ToListAsync(ct);
    }

    [HttpGet("{id:guid}/logs")]
    public async Task<PagedResult<LogView>> Logs(
        Guid id, [FromQuery] PageQuery page, [FromQuery] LogSeverity? minSeverity, [FromQuery] string? search,
        [FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to, CancellationToken ct)
    {
        var q = db.Logs.AsNoTracking().Where(l => l.AtmId == id);
        if (minSeverity is { } sev)
        {
            var allowed = Enum.GetValues<LogSeverity>().Where(s => s >= sev).ToList();
            q = q.Where(l => allowed.Contains(l.Severity));
        }

        if (from is { } f)
        {
            q = q.Where(l => l.Timestamp >= f);
        }

        if (to is { } t)
        {
            q = q.Where(l => l.Timestamp <= t);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            q = q.Where(l => l.Message.ToLower().Contains(term) || l.Source.ToLower().Contains(term));
        }

        var total = await q.CountAsync(ct);
        var items = await q.OrderByDescending(l => l.Timestamp).ThenByDescending(l => l.Id).Skip(page.Skip).Take(page.PageSize)
            .Select(l => new LogView(l.Id, l.Timestamp, l.Severity, l.Source, l.Message)).ToListAsync(ct);
        return new PagedResult<LogView>(items, total, page.Page, page.PageSize);
    }

    [HttpGet("{id:guid}/commands")]
    public async Task<PagedResult<CommandView>> Commands(Guid id, [FromQuery] PageQuery page, CancellationToken ct)
    {
        var q = db.Commands.AsNoTracking().Include(c => c.Atm).Where(c => c.AtmId == id);
        var total = await q.CountAsync(ct);
        var items = await q.OrderByDescending(c => c.CreatedAt).Skip(page.Skip).Take(page.PageSize).ToListAsync(ct);
        return new PagedResult<CommandView>(items.Select(c => c.ToView()).ToList(), total, page.Page, page.PageSize);
    }

    [HttpPost("{id:guid}/commands")]
    [Authorize(Policy = Policies.Operator)]
    public async Task<ActionResult<CommandView>> IssueCommand(Guid id, CreateCommandRequest request, CancellationToken ct)
    {
        var command = await commands.CreateAsync(id, request.Type, request.Parameters, request.Reason, User.UserName(), User.Role(), ct);
        return Accepted($"/api/commands/{command.Id}", command.ToView());
    }

    private async Task<IActionResult> SetEnabled(Guid id, bool enabled, CancellationToken ct)
    {
        var atm = await db.Atms.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (atm is null)
        {
            return NotFound();
        }

        atm.IsEnabled = enabled;
        audit.Record(enabled ? "atm.enable" : "atm.disable", "Atm", atm.TerminalId);
        await db.SaveChangesAsync(ct);
        if (!enabled)
        {
            agents.Disconnect(id);
        }

        return NoContent();
    }

    private static void Apply(Atm atm, UpsertAtmRequest r)
    {
        atm.Name = r.Name;
        atm.Branch = r.Branch;
        atm.Address = r.Address;
        atm.City = r.City;
        atm.Latitude = r.Latitude;
        atm.Longitude = r.Longitude;
        atm.Vendor = r.Vendor;
        atm.Model = r.Model;
        atm.SerialNumber = r.SerialNumber;
    }
}
