using AtmMonitor.Api.Auth;
using AtmMonitor.Api.Models;
using AtmMonitor.Contracts;
using AtmMonitor.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AtmMonitor.Api.Controllers;

[ApiController]
[Route("api/dashboard")]
[Authorize(Policy = Policies.Viewer)]
public sealed class DashboardController(AppDbContext db) : ControllerBase
{
    [HttpGet("summary")]
    public async Task<DashboardSummary> Summary(CancellationToken ct)
    {
        var byStatus = await db.Atms.AsNoTracking().GroupBy(a => a.Status)
            .Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);

        var alertsBySeverity = await db.Alerts.AsNoTracking().Where(a => a.Status != AlertStatus.Resolved).GroupBy(a => a.Severity)
            .Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);

        // Summed in memory: cassette rows are small and this keeps decimal math exact on every provider.
        var cassettes = await db.Cassettes.AsNoTracking()
            .Where(c => c.Type == CassetteType.Dispense || c.Type == CassetteType.Recycle)
            .Select(c => new { c.AtmId, c.Currency, c.Denomination, c.Count, c.Status })
            .ToListAsync(ct);

        var cash = cassettes.Where(c => c.Status != CassetteStatus.Missing).GroupBy(c => c.Currency)
            .ToDictionary(g => g.Key, g => g.Sum(c => c.Denomination * c.Count));
        var lowCash = cassettes.Where(c => c.Status is CassetteStatus.Low or CassetteStatus.Empty).Select(c => c.AtmId).Distinct().Count();

        var pending = await db.Commands.CountAsync(c =>
            c.Status == CommandStatus.Pending || c.Status == CommandStatus.Sent ||
            c.Status == CommandStatus.Acknowledged || c.Status == CommandStatus.Running, ct);

        var recent = await db.Alerts.AsNoTracking().Include(a => a.Atm).Where(a => a.Status != AlertStatus.Resolved)
            .OrderByDescending(a => a.RaisedAt).Take(10).ToListAsync(ct);

        return new DashboardSummary(byStatus.Values.Sum(), byStatus, alertsBySeverity, cash, lowCash, pending,
            recent.Select(a => a.ToView()).ToList());
    }
}
