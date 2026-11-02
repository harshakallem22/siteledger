namespace SiteLedger.Application.Contracts.Reports;

// Actuals count Approved only; Pending is informational; Rejected is ignored.
public sealed record ProjectBudgetReportResponse(
    Guid ProjectId,
    string ProjectCode,
    string ProjectName,
    decimal BudgetUsd,
    decimal ApprovedLaborActualUsd,
    decimal ApprovedExpenseActualUsd,
    decimal TotalActualUsd,
    decimal PendingLaborUsd,
    decimal PendingExpenseUsd,
    decimal RemainingBudgetUsd,
    decimal BudgetUsedPct,
    bool IsOverBudget,
    DateTimeOffset GeneratedAtUtc);
