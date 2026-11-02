using SiteLedger.Application.Contracts.Reports;

namespace SiteLedger.Application.Services;

public interface IReportService
{
    Task<ProjectBudgetReportResponse> GetProjectReportAsync(Guid projectId, CancellationToken ct);

    Task<PortfolioReportResponse> GetPortfolioReportAsync(PortfolioQuery query, CancellationToken ct);
}
