using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiteLedger.Application.Abstractions;
using SiteLedger.Application.Contracts.Common;
using SiteLedger.Application.Contracts.Timesheets;
using SiteLedger.Application.Exceptions;
using SiteLedger.Domain.Entities;

namespace SiteLedger.Application.Services;

public sealed class TimesheetService(
    ISiteLedgerDbContext db,
    ICurrentUser currentUser,
    TimeProvider clock,
    SubmissionDecider decider,
    ILogger<TimesheetService> logger) : ITimesheetService
{
    private static readonly Expression<Func<Timesheet, TimesheetResponse>> ToResponseExpression = t =>
        new TimesheetResponse(
            t.Id,
            t.ProjectId,
            t.EmployeeId,
            t.WorkDate,
            t.Hours,
            t.HourlyRateSnapshotUsd,
            t.LaborCostUsd,
            t.Status,
            t.SubmittedBySubject,
            t.SubmittedAtUtc,
            t.DecidedBySubject,
            t.DecidedAtUtc);

    private static readonly Func<Timesheet, TimesheetResponse> ToResponse = ToResponseExpression.Compile();

    public async Task<TimesheetResponse> SubmitAsync(Guid projectId, SubmitTimesheetRequest request, CancellationToken ct)
    {
        var project = await db.Projects.AsNoTracking().FirstOrDefaultAsync(p => p.Id == projectId, ct)
            ?? throw new ResourceNotFoundException("Project", projectId);

        var employeeId = request.EmployeeId ?? Guid.Empty;
        var employee = await db.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.Id == employeeId, ct)
            ?? throw new ResourceNotFoundException("Employee", employeeId);

        var timesheet = Timesheet.Submit(
            project,
            employee,
            request.WorkDate ?? DateOnly.MinValue,
            request.Hours ?? 0m,
            currentUser.Subject,
            clock.GetUtcNow());

        db.Timesheets.Add(timesheet);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DuplicateResourceException ex)
        {
            throw new DuplicateResourceException(
                $"Employee '{employeeId}' already has a timesheet for {timesheet.WorkDate:yyyy-MM-dd} on this project.", ex);
        }

        logger.LogInformation(
            "Timesheet {TimesheetId} submitted for project {ProjectId}: {Hours} h, cost {LaborCostUsd}",
            timesheet.Id,
            projectId,
            timesheet.Hours,
            timesheet.LaborCostUsd);

        return ToResponse(timesheet);
    }

    public async Task<PagedResponse<TimesheetResponse>> ListAsync(Guid projectId, SubmissionQuery query, CancellationToken ct)
    {
        Paging.Validate(query.Page, query.PageSize);

        if (!await db.Projects.AnyAsync(p => p.Id == projectId, ct))
        {
            throw new ResourceNotFoundException("Project", projectId);
        }

        var timesheets = db.Timesheets.AsNoTracking().Where(t => t.ProjectId == projectId);
        if (query.Status is { } status)
        {
            timesheets = timesheets.Where(t => t.Status == status);
        }

        var totalCount = await timesheets.CountAsync(ct);
        var items = await timesheets
            .OrderByDescending(t => t.SubmittedAtUtc)
            .ThenBy(t => t.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(ToResponseExpression)
            .ToListAsync(ct);

        return new PagedResponse<TimesheetResponse>(items, query.Page, query.PageSize, totalCount);
    }

    public Task<TimesheetResponse> ApproveAsync(Guid id, string? note, CancellationToken ct) =>
        DecideAsync(id, (timesheet, actor, now) => timesheet.Approve(actor, note, now), ct);

    public Task<TimesheetResponse> RejectAsync(Guid id, string? reason, CancellationToken ct) =>
        DecideAsync(id, (timesheet, actor, now) => timesheet.Reject(actor, reason, now), ct);

    private async Task<TimesheetResponse> DecideAsync(
        Guid id,
        Func<Timesheet, string, DateTimeOffset, ApprovalAudit> decide,
        CancellationToken ct)
    {
        var timesheet = await decider.DecideAsync(db.Timesheets, "Timesheet", id, decide, ct);

        logger.LogInformation("Timesheet decision completed for {TimesheetId} with {Decision}", timesheet.Id, timesheet.Status);
        return ToResponse(timesheet);
    }
}
