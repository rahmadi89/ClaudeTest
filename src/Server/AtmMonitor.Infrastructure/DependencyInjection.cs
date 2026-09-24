using AtmMonitor.Domain.Entities;
using AtmMonitor.Domain.Services;
using AtmMonitor.Infrastructure.Alerts;
using AtmMonitor.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AtmMonitor.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        var provider = config["Database:Provider"] ?? "Postgres";
        var connectionString = config.GetConnectionString("Default")
            ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured.");

        services.AddDbContext<AppDbContext>(o =>
        {
            if (provider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase))
            {
                o.UseSqlite(connectionString);
            }
            else
            {
                o.UseNpgsql(connectionString, npg => npg.EnableRetryOnFailure(5));
            }
        });

        services.Configure<AlertThresholds>(config.GetSection("Alerts"));
        services.AddScoped<AlertManager>();
        services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
        services.AddSingleton(TimeProvider.System);
        return services;
    }
}
