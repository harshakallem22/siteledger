using System.Globalization;
using SiteLedger.Domain.Entities;
using SiteLedger.Domain.Enums;
using SiteLedger.Domain.Exceptions;

namespace SiteLedger.UnitTests.Domain;

public sealed class ExpenseRulesTests
{
    private const decimal MaxAmount = 1_000_000.00m;
    private static readonly DateTimeOffset Now = new(2026, 10, 28, 16, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 10, 28);

    private static Project ActiveProject() =>
        Project.Create("CON-2026-001", "Fremont Equipment Bay Upgrade", 250_000m, ProjectStatus.Active, Now);

    private static Expense Submit(
        string description = "Electrical conduit and mounting hardware",
        ExpenseCategory category = ExpenseCategory.Materials,
        decimal amount = 1_250.00m,
        DateOnly? date = null,
        string? reference = "PO-1873",
        Project? project = null) =>
        Expense.Submit(project ?? ActiveProject(), description, category, amount, date ?? Today.AddDays(-1), reference, MaxAmount, "pm-1", Now);

    [Fact]
    public void Submit_Valid_TrimsText_AndStartsPending()
    {
        var expense = Submit(description: "  Electrical conduit and mounting hardware  ", reference: "  PO-1873  ");

        Assert.Equal("Electrical conduit and mounting hardware", expense.Description);
        Assert.Equal("PO-1873", expense.ExternalReference);
        Assert.Equal(ExpenseCategory.Materials, expense.Category);
        Assert.Equal(1_250.00m, expense.AmountUsd);
        Assert.Equal(SubmissionStatus.Pending, expense.Status);
        Assert.Equal("pm-1", expense.SubmittedBySubject);
        Assert.Null(expense.DecidedBySubject);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public void Submit_BlankExternalReference_IsStoredAsNull(string? reference)
    {
        Assert.Null(Submit(reference: reference).ExternalReference);
    }

    [Fact]
    public void Submit_AcceptsBoundaries_TodayAndConfiguredMaximum()
    {
        var expense = Submit(amount: MaxAmount, date: Today);

        Assert.Equal(MaxAmount, expense.AmountUsd);
        Assert.Equal(Today, expense.ExpenseDate);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5.00")]
    [InlineData("10.001")]
    [InlineData("1000000.01")]  // one cent above the configured maximum
    public void Submit_RejectsInvalidAmounts(string amount)
    {
        var ex = Assert.Throws<DomainValidationException>(() => Submit(amount: decimal.Parse(amount, CultureInfo.InvariantCulture)));

        Assert.Equal("amountUsd", ex.Field);
    }

    [Fact]
    public void Submit_UsesTheConfiguredMaximum_NotAHardcodedOne()
    {
        var ex = Assert.Throws<DomainValidationException>(() =>
            Expense.Submit(ActiveProject(), "Small tools", ExpenseCategory.Equipment, 600m, Today, null, maxAmountUsd: 500m, "pm-1", Now));

        Assert.Equal("amountUsd", ex.Field);
    }

    [Theory]
    [InlineData("")]
    [InlineData("ab")]
    [InlineData("   ab   ")]
    public void Submit_RejectsDescriptionsShorterThan3Characters(string description)
    {
        var ex = Assert.Throws<DomainValidationException>(() => Submit(description: description));

        Assert.Equal("description", ex.Field);
    }

    [Fact]
    public void Submit_RejectsOverlongDescriptionAndReference()
    {
        Assert.Equal("description", Assert.Throws<DomainValidationException>(() => Submit(description: new string('d', 201))).Field);
        Assert.Equal("externalReference", Assert.Throws<DomainValidationException>(() => Submit(reference: new string('r', 81))).Field);
    }

    [Fact]
    public void Submit_RejectsUndefinedCategory()
    {
        var ex = Assert.Throws<DomainValidationException>(() => Submit(category: (ExpenseCategory)99));

        Assert.Equal("category", ex.Field);
    }

    [Fact]
    public void Submit_RejectsFutureExpenseDate()
    {
        var ex = Assert.Throws<DomainValidationException>(() => Submit(date: Today.AddDays(1)));

        Assert.Equal("expenseDate", ex.Field);
    }

    [Fact]
    public void Submit_IsAConflict_WhenProjectIsNotActive()
    {
        var draft = Project.Create("CON-2026-002", "Battery Line", 1_000m, ProjectStatus.Draft, Now);

        Assert.Throws<DomainConflictException>(() => Submit(project: draft));
    }
}
