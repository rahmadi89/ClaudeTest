using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AtmMonitor.Infrastructure.Persistence;

/// <summary>
/// Used only by <c>dotnet ef</c> to generate PostgreSQL migrations. No database connection is made when adding migrations.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var cs = Environment.GetEnvironmentVariable("ATMMONITOR_DESIGN_CONNECTION")
                 ?? "Host=localhost;Database=atm_monitor;Username=atm_monitor;Password=design-time-only";
        return new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(cs).Options);
    }
}
