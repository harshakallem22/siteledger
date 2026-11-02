using SiteLedger.Domain.Enums;

namespace SiteLedger.Application.Contracts.Timesheets;

public sealed record TimesheetResponse(
    Guid Id,
    Guid ProjectId,
    Guid EmployeeId,
    DateOnly WorkDate,
    decimal Hours,
    decimal HourlyRateSnapshotUsd,
    decimal LaborCostUsd,
    SubmissionStatus Status,
    string SubmittedBySubject,
    DateTimeOffset SubmittedAtUtc,
    string? DecidedBySubject,
    DateTimeOffset? DecidedAtUtc);
