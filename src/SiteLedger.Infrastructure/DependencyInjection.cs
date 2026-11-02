using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SiteLedger.Application.Abstractions;
using SiteLedger.Infrastructure.Persistence;

namespace SiteLedger.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<SiteLedgerDbContext>(options => options
            .UseSqlServer(connectionString, sql =>
                // Azure SQL drops connections briefly (e.g. resuming from auto-pause); retry those.
                sql.EnableRetryOnFailure())
            // Failures are logged once by the API's handler; EF's copies are demoted so expected 409s don't look like errors.
            .ConfigureWarnings(warnings => warnings.Log(
                (CoreEventId.SaveChangesFailed, LogLevel.Debug),
                (RelationalEventId.CommandError, LogLevel.Warning))));

        // Services ask for the interface; both resolve to the same per-request DbContext.
        services.AddScoped<ISiteLedgerDbContext>(provider => provider.GetRequiredService<SiteLedgerDbContext>());

        return services;
    }
}
