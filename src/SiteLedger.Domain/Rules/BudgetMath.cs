namespace SiteLedger.Domain.Rules;

public static class BudgetMath
{
    // Fixed once at submission and stored, so SQL sums and .NET never disagree on rounding (x.xx5 rounds up).
    public static decimal CalculateLaborCost(decimal hours, decimal hourlyRateUsd) =>
        decimal.Round(hours * hourlyRateUsd, 2, MidpointRounding.AwayFromZero);

    // For a portfolio pass the sums (weighted), never an average of percentages; a zero budget reports 0.
    public static decimal UsedPercent(decimal actualUsd, decimal budgetUsd) =>
        budgetUsd == 0 ? 0m : decimal.Round(100m * actualUsd / budgetUsd, 2, MidpointRounding.AwayFromZero);

    // Spending exactly the budget is not over budget.
    public static bool IsOverBudget(decimal actualUsd, decimal budgetUsd) => actualUsd > budgetUsd;
}
