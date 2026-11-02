using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SiteLedger.Application.Services;

namespace SiteLedger.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // .NET's built-in clock abstraction; tests can substitute a fixed time.
        services.TryAddSingleton(TimeProvider.System);

        services.AddScoped<SubmissionDecider>();
        services.AddScoped<IProjectService, ProjectService>();
        services.AddScoped<ITimesheetService, TimesheetService>();
        services.AddScoped<IExpenseService, ExpenseService>();
        services.AddScoped<IReportService, ReportService>();

        return services;
    }
}
