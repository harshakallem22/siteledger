namespace SiteLedger.Application.Contracts.Reports;

// Totals cover ALL matching projects, not just the page; percent is weighted (Σ actual / Σ budget).
public sealed record PortfolioReportResponse(
    int TotalProjectsMatchingFilter,
    decimal PortfolioBudgetUsd,
    decimal PortfolioApprovedActualUsd,
    decimal PortfolioRemainingUsd,
    decimal PortfolioBudgetUsedPct,
    int OverBudgetProjectCount,
    int Page,
    int PageSize,
    IReadOnlyList<PortfolioProjectItem> Items);

public sealed record PortfolioProjectItem(
    Guid ProjectId,
    string ProjectCode,
    decimal BudgetUsd,
    decimal ApprovedActualUsd,
    decimal RemainingBudgetUsd,
    bool IsOverBudget);
