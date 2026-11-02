using System.Globalization;
using SiteLedger.Domain.Rules;

namespace SiteLedger.UnitTests.Domain;

public sealed class BudgetMathTests
{
    [Theory]
    [InlineData("8.00", "55.00", "440.00")]     // spec story: 8 h × $55 = $440
    [InlineData("7.25", "55.55", "402.74")]     // 402.7375 → rounds to cents
    [InlineData("2.50", "10.01", "25.03")]      // 25.025 exactly half a cent → away from zero (banker's rounding would give 25.02)
    [InlineData("0.10", "0.25", "0.03")]        // 0.025 → 0.03
    [InlineData("0.01", "0.01", "0.00")]        // 0.0001 → 0.00
    [InlineData("24.00", "95.00", "2280.00")]   // largest seeded day
    public void CalculateLaborCost_RoundsToCentsWithHalfAwayFromZero(string hours, string rate, string expected)
    {
        var cost = BudgetMath.CalculateLaborCost(Parse(hours), Parse(rate));

        Assert.Equal(Parse(expected), cost);
    }

    [Theory]
    [InlineData("0", "250000", "0")]            // U01: nothing approved yet
    [InlineData("1690", "250000", "0.68")]      // spec story: 0.676 → 0.68
    [InlineData("250000", "250000", "100.00")]  // exactly on budget
    [InlineData("1250", "1000", "125.00")]      // U04: overrun
    [InlineData("1", "3", "33.33")]             // 33.333… → 33.33
    [InlineData("0", "0", "0")]                 // empty portfolio: no division by zero
    public void UsedPercent_IsRoundedToTwoDecimals(string actual, string budget, string expected)
    {
        Assert.Equal(Parse(expected), BudgetMath.UsedPercent(Parse(actual), Parse(budget)));
    }

    [Theory]
    [InlineData("0", "250000", false)]          // U01
    [InlineData("250000", "250000", false)]     // spending exactly the budget is fine
    [InlineData("250000.01", "250000", true)]   // U04: one cent over
    public void IsOverBudget_OnlyWhenActualExceedsBudget(string actual, string budget, bool expected)
    {
        Assert.Equal(expected, BudgetMath.IsOverBudget(Parse(actual), Parse(budget)));
    }

    [Fact] // U05
    public void PortfolioPercent_IsWeightedBySums_NotAnAverageOfProjectPercents()
    {
        // Small project fully spent, large project untouched.
        (decimal Budget, decimal Actual)[] projects = [(100m, 100m), (900m, 0m)];

        var weighted = BudgetMath.UsedPercent(projects.Sum(p => p.Actual), projects.Sum(p => p.Budget));
        var naiveAverage = projects.Average(p => BudgetMath.UsedPercent(p.Actual, p.Budget));

        Assert.Equal(10.00m, weighted);     // $100 of $1,000 spent
        Assert.Equal(50.00m, naiveAverage); // misleading: (100% + 0%) / 2
    }

    private static decimal Parse(string value) => decimal.Parse(value, CultureInfo.InvariantCulture);
}
