using System.Globalization;
using SiteLedger.Domain.Entities;
using SiteLedger.Domain.Enums;
using SiteLedger.Domain.Exceptions;

namespace SiteLedger.UnitTests.Domain;

public sealed class ProjectRulesTests
{
    private static readonly DateTimeOffset CreatedAt = new(2026, 10, 28, 16, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Later = CreatedAt.AddHours(2);

    private static Project NewProject(ProjectStatus status = ProjectStatus.Active) =>
        Project.Create("CON-2026-001", "Fremont Equipment Bay Upgrade", 250_000.00m, status, CreatedAt);

    [Fact]
    public void Create_NormalizesCodeToUpperCaseAndTrimsName()
    {
        var project = Project.Create("  con-2026-001 ", "  Fremont Equipment Bay Upgrade  ", 250_000.00m, ProjectStatus.Active, CreatedAt);

        Assert.Equal("CON-2026-001", project.ProjectCode);
        Assert.Equal("Fremont Equipment Bay Upgrade", project.Name);
        Assert.Equal(250_000.00m, project.BudgetUsd);
        Assert.Equal(ProjectStatus.Active, project.Status);
        Assert.Equal(CreatedAt, project.CreatedAtUtc);
        Assert.Equal(CreatedAt, project.UpdatedAtUtc);
        Assert.NotEqual(Guid.Empty, project.Id);
    }

    [Theory]
    [InlineData("")]
    [InlineData("AB")]                         // too short
    [InlineData("CON--2026")]                  // double hyphen
    [InlineData("-CON-2026")]                  // leading hyphen
    [InlineData("CON_2026_001")]               // underscore not allowed
    [InlineData("CON-2026-001-ABCDEFGHIJKLM")] // 25 characters
    public void Create_RejectsInvalidProjectCode(string code)
    {
        var ex = Assert.Throws<DomainValidationException>(() =>
            Project.Create(code, "Valid Name", 1_000m, ProjectStatus.Active, CreatedAt));

        Assert.Equal("projectCode", ex.Field);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1.00")]
    [InlineData("100.001")]          // more than 2 decimal places
    [InlineData("1000000000.01")]    // above the cap
    public void Create_RejectsInvalidBudget(string budget)
    {
        var ex = Assert.Throws<DomainValidationException>(() =>
            Project.Create("CON-2026-001", "Valid Name", decimal.Parse(budget, CultureInfo.InvariantCulture), ProjectStatus.Active, CreatedAt));

        Assert.Equal("budgetUsd", ex.Field);
    }

    [Fact]
    public void Create_RejectsCompletedStatus()
    {
        var ex = Assert.Throws<DomainValidationException>(() => NewProject(ProjectStatus.Completed));

        Assert.Equal("status", ex.Field);
    }

    [Fact]
    public void ChangeStatus_FollowsDraftToActiveToCompleted()
    {
        var project = NewProject(ProjectStatus.Draft);

        project.ChangeStatus(ProjectStatus.Active, hasPendingSubmissions: false, Later);
        Assert.Equal(ProjectStatus.Active, project.Status);

        project.ChangeStatus(ProjectStatus.Completed, hasPendingSubmissions: false, Later);
        Assert.Equal(ProjectStatus.Completed, project.Status);
        Assert.Equal(Later, project.UpdatedAtUtc);
    }

    [Theory]
    [InlineData(ProjectStatus.Active, ProjectStatus.Draft)]
    [InlineData(ProjectStatus.Draft, ProjectStatus.Completed)]
    public void ChangeStatus_RejectsTransitionsOutsideTheLifecycle(ProjectStatus from, ProjectStatus to)
    {
        var project = NewProject(from);

        Assert.Throws<DomainConflictException>(() => project.ChangeStatus(to, hasPendingSubmissions: false, Later));
        Assert.Equal(from, project.Status);
    }

    [Fact]
    public void ChangeStatus_ToCompleted_IsBlockedWhileSubmissionsArePending()
    {
        var project = NewProject();

        Assert.Throws<DomainConflictException>(() => project.ChangeStatus(ProjectStatus.Completed, hasPendingSubmissions: true, Later));
        Assert.Equal(ProjectStatus.Active, project.Status);
    }

    [Fact]
    public void ChangeStatus_ToSameStatus_IsANoOp()
    {
        var project = NewProject();

        project.ChangeStatus(ProjectStatus.Active, hasPendingSubmissions: true, Later);

        Assert.Equal(CreatedAt, project.UpdatedAtUtc);
    }

    [Fact]
    public void CompletedProject_CannotBeRenamedRebudgetedOrReopened()
    {
        var project = NewProject();
        project.ChangeStatus(ProjectStatus.Completed, hasPendingSubmissions: false, Later);

        Assert.Throws<DomainConflictException>(() => project.Rename("New Name", Later));
        Assert.Throws<DomainConflictException>(() => project.ChangeBudget(1m, Later));
        Assert.Throws<DomainConflictException>(() => project.ChangeStatus(ProjectStatus.Active, hasPendingSubmissions: false, Later));
    }

    [Fact]
    public void ChangeBudget_AllowsAnyValidPositiveAmount()
    {
        var project = NewProject();

        project.ChangeBudget(260_000.50m, Later);

        Assert.Equal(260_000.50m, project.BudgetUsd);
        Assert.Equal(Later, project.UpdatedAtUtc);
    }
}
