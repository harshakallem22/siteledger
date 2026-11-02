using System.ComponentModel.DataAnnotations;

namespace SiteLedger.Application.Contracts.Timesheets;

// Client-owned fields only; rate, cost, status and submitter are server-owned (extra JSON is ignored).
public sealed record SubmitTimesheetRequest
{
    [Required]
    public Guid? EmployeeId { get; init; }

    [Required]
    public DateOnly? WorkDate { get; init; }

    [Required]
    public decimal? Hours { get; init; }
}
