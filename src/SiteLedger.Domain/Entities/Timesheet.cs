using SiteLedger.Domain.Enums;
using SiteLedger.Domain.Exceptions;
using SiteLedger.Domain.Rules;

namespace SiteLedger.Domain.Entities;

public sealed class Timesheet : Submission
{
    public const decimal MaxHoursPerDay = 24m;

    private Timesheet()
    {
    }

    private Timesheet(Guid projectId, Employee employee, DateOnly workDate, decimal hours, string submittedBySubject, DateTimeOffset submittedAtUtc)
        : base(projectId, submittedBySubject, submittedAtUtc)
    {
        EmployeeId = employee.Id;
        WorkDate = workDate;
        Hours = hours;
        HourlyRateSnapshotUsd = employee.HourlyRateUsd;
        LaborCostUsd = BudgetMath.CalculateLaborCost(hours, employee.HourlyRateUsd);
    }

    public Guid EmployeeId { get; private set; }

    public DateOnly WorkDate { get; private set; }

    public decimal Hours { get; private set; }

    public decimal HourlyRateSnapshotUsd { get; private set; }

    public decimal LaborCostUsd { get; private set; }

    protected override SubmissionType AuditType => SubmissionType.Timesheet;

    // Rate is copied from the employee (never the client) and the cost is computed once; dates use UTC.
    public static Timesheet Submit(
        Project project,
        Employee employee,
        DateOnly workDate,
        decimal hours,
        string submittedBySubject,
        DateTimeOffset nowUtc)
    {
        if (project.Status != ProjectStatus.Active)
        {
            throw new DomainConflictException($"Timesheets can only be submitted to Active projects; this project is {project.Status}.");
        }

        if (!employee.IsActive)
        {
            throw new DomainConflictException("Timesheets cannot be submitted for an inactive employee.");
        }

        if (hours <= 0 || hours > MaxHoursPerDay || decimal.Round(hours, 2) != hours)
        {
            throw new DomainValidationException("hours", $"Hours must be greater than 0 and at most {MaxHoursPerDay}, with at most 2 decimal places.");
        }

        if (workDate > DateOnly.FromDateTime(nowUtc.UtcDateTime))
        {
            throw new DomainValidationException("workDate", "Work date cannot be in the future (UTC).");
        }

        return new Timesheet(project.Id, employee, workDate, hours, submittedBySubject, nowUtc);
    }
}
