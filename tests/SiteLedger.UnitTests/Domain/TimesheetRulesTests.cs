using System.Globalization;
using SiteLedger.Domain.Entities;
using SiteLedger.Domain.Enums;
using SiteLedger.Domain.Exceptions;

namespace SiteLedger.UnitTests.Domain;

public sealed class TimesheetRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 28, 16, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 10, 28);

    private static readonly Employee ActiveEmployee =
        new(Guid.NewGuid(), "Priya Patel", "priya@siteledger.example", 55.55m, isActive: true, Now);

    private static Project ActiveProject() =>
        Project.Create("CON-2026-001", "Fremont Equipment Bay Upgrade", 250_000m, ProjectStatus.Active, Now);

    [Fact]
    public void Submit_SnapshotsEmployeeRate_ComputesCost_AndStartsPending()
    {
        var project = ActiveProject();

        var timesheet = Timesheet.Submit(project, ActiveEmployee, Today.AddDays(-1), 7.25m, "pm-1", Now);

        Assert.Equal(project.Id, timesheet.ProjectId);
        Assert.Equal(ActiveEmployee.Id, timesheet.EmployeeId);
        Assert.Equal(55.55m, timesheet.HourlyRateSnapshotUsd);
        Assert.Equal(402.74m, timesheet.LaborCostUsd);
        Assert.Equal(SubmissionStatus.Pending, timesheet.Status);
        Assert.Equal("pm-1", timesheet.SubmittedBySubject);
        Assert.Null(timesheet.DecidedBySubject);
        Assert.Null(timesheet.DecidedAtUtc);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("24.01")]
    [InlineData("8.001")]
    public void Submit_RejectsInvalidHours(string hours)
    {
        var ex = Assert.Throws<DomainValidationException>(() =>
            Timesheet.Submit(ActiveProject(), ActiveEmployee, Today, decimal.Parse(hours, CultureInfo.InvariantCulture), "pm-1", Now));

        Assert.Equal("hours", ex.Field);
    }

    [Fact]
    public void Submit_AcceptsBoundaryValues_TodayAnd24Hours()
    {
        var timesheet = Timesheet.Submit(ActiveProject(), ActiveEmployee, Today, 24.00m, "pm-1", Now);

        Assert.Equal(Today, timesheet.WorkDate);
        Assert.Equal(24.00m, timesheet.Hours);
    }

    [Fact]
    public void Submit_RejectsFutureWorkDate()
    {
        var ex = Assert.Throws<DomainValidationException>(() =>
            Timesheet.Submit(ActiveProject(), ActiveEmployee, Today.AddDays(1), 8m, "pm-1", Now));

        Assert.Equal("workDate", ex.Field);
    }

    [Theory]
    [InlineData(ProjectStatus.Draft)]
    [InlineData(ProjectStatus.Completed)]
    public void Submit_IsAConflict_WhenProjectIsNotActive(ProjectStatus status)
    {
        var project = Project.Create("CON-2026-002", "Battery Line", 1_000m, ProjectStatus.Draft, Now);
        if (status == ProjectStatus.Completed)
        {
            project.ChangeStatus(ProjectStatus.Active, hasPendingSubmissions: false, Now);
            project.ChangeStatus(ProjectStatus.Completed, hasPendingSubmissions: false, Now);
        }

        Assert.Throws<DomainConflictException>(() => Timesheet.Submit(project, ActiveEmployee, Today, 8m, "pm-1", Now));
    }

    [Fact]
    public void Submit_IsAConflict_ForInactiveEmployee()
    {
        var formerEmployee = new Employee(Guid.NewGuid(), "Liam Chen", "liam@siteledger.example", 40m, isActive: false, Now);

        Assert.Throws<DomainConflictException>(() => Timesheet.Submit(ActiveProject(), formerEmployee, Today, 8m, "pm-1", Now));
    }
}
