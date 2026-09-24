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
[Route("api/commands")]
[Authorize(Policy = Policies.Viewer)]
public sealed class CommandsController(AppDbContext db, CommandService commands) : ControllerBase
{
    [HttpGet]
    public async Task<PagedResult<CommandView>> List([FromQuery] PageQuery page, [FromQuery] CommandStatus? status, [FromQuery] Guid? atmId, CancellationToken ct)
    {
        var q = db.Commands.AsNoTracking().Include(c => c.Atm).AsQueryable();
        if (status is { } s)
        {
            q = q.Where(c => c.Status == s);
        }

        if (atmId is { } a)
        {
            q = q.Where(c => c.AtmId == a);
        }

        var total = await q.CountAsync(ct);
        var items = await q.OrderByDescending(c => c.CreatedAt).Skip(page.Skip).Take(page.PageSize).ToListAsync(ct);
        return new PagedResult<CommandView>(items.Select(c => c.ToView()).ToList(), total, page.Page, page.PageSize);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<CommandView>> Get(Guid id, CancellationToken ct)
    {
        var c = await db.Commands.AsNoTracking().Include(x => x.Atm).FirstOrDefaultAsync(x => x.Id == id, ct);
        return c is null ? NotFound() : c.ToView();
    }

    [HttpPost("{id:guid}/cancel")]
    [Authorize(Policy = Policies.Operator)]
    public async Task<CommandView> Cancel(Guid id, CancellationToken ct) => (await commands.CancelAsync(id, ct)).ToView();
}
