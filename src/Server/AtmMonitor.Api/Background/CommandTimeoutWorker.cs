using AtmMonitor.Api.Services;
using AtmMonitor.Contracts;
using AtmMonitor.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AtmMonitor.Api.Background;

public sealed class CommandTimeoutWorker(IServiceScopeFactory scopes, ILogger<CommandTimeoutWorker> logger)
    : PeriodicWorker(scopes, logger)
{
    protected override TimeSpan Interval => TimeSpan.FromSeconds(30);

    protected override async Task RunOnceAsync(IServiceProvider sp, CancellationToken ct)
    {
        var db = sp.GetRequiredService<AppDbContext>();
        var now = sp.GetRequiredService<TimeProvider>().GetUtcNow();
        var notifier = sp.GetRequiredService<DashboardNotifier>();

        var expired = await db.Commands
            .Where(c => c.ExpiresAt < now &&
                        (c.Status == CommandStatus.Pending || c.Status == CommandStatus.Sent ||
                         c.Status == CommandStatus.Acknowledged || c.Status == CommandStatus.Running))
            .OrderBy(c => c.ExpiresAt)
            .Take(500)
            .ToListAsync(ct);

        foreach (var c in expired)
        {
            c.TryTransition(CommandStatus.TimedOut, now, error: "No result received before the command expired.");
            try
            {
                await db.SaveChangesAsync(ct);
                await notifier.CommandChanged(c);
            }
            catch (DbUpdateConcurrencyException)
            {
                db.ChangeTracker.Clear();
            }
        }
    }
}
