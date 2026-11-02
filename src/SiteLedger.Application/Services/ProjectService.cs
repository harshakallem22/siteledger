using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiteLedger.Application.Abstractions;
using SiteLedger.Application.Contracts.Common;
using SiteLedger.Application.Contracts.Projects;
using SiteLedger.Application.Exceptions;
using SiteLedger.Domain.Entities;
using SiteLedger.Domain.Enums;

namespace SiteLedger.Application.Services;

public sealed class ProjectService(ISiteLedgerDbContext db, TimeProvider clock, ILogger<ProjectService> logger)
    : IProjectService
{
    // Expression form is translated to SQL; the compiled form maps an entity already in memory.
    private static readonly Expression<Func<Project, ProjectResponse>> ToResponseExpression = p =>
        new ProjectResponse(p.Id, p.ProjectCode, p.Name, p.BudgetUsd, p.Status, p.CreatedAtUtc, p.UpdatedAtUtc);

    private static readonly Func<Project, ProjectResponse> ToResponse = ToResponseExpression.Compile();

    public async Task<ProjectResponse> CreateAsync(CreateProjectRequest request, CancellationToken ct)
    {
        var project = Project.Create(
            request.ProjectCode ?? string.Empty,
            request.Name ?? string.Empty,
            request.BudgetUsd ?? 0m,
            request.Status ?? ProjectStatus.Active,
            clock.GetUtcNow());

        db.Projects.Add(project);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DuplicateResourceException ex)
        {
            // The unique index, not an "exists?" pre-check, guarantees uniqueness under concurrency.
            throw new DuplicateResourceException($"Project code '{project.ProjectCode}' already exists.", ex);
        }

        logger.LogInformation("Project {ProjectId} created with code {ProjectCode}", project.Id, project.ProjectCode);
        return ToResponse(project);
    }

    public async Task<PagedResponse<ProjectResponse>> ListAsync(ProjectQuery query, CancellationToken ct)
    {
        Paging.Validate(query.Page, query.PageSize);

        var projects = db.Projects.AsNoTracking();
        if (query.Status is { } status)
        {
            projects = projects.Where(p => p.Status == status);
        }

        // Filter, count, sort and page all run in SQL.
        var totalCount = await projects.CountAsync(ct);
        var items = await projects
            .OrderBy(p => p.ProjectCode)
            .ThenBy(p => p.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(ToResponseExpression)
            .ToListAsync(ct);

        return new PagedResponse<ProjectResponse>(items, query.Page, query.PageSize, totalCount);
    }

    public async Task<ProjectResponse> GetAsync(Guid id, CancellationToken ct) =>
        await db.Projects.AsNoTracking()
            .Where(p => p.Id == id)
            .Select(ToResponseExpression)
            .FirstOrDefaultAsync(ct)
        ?? throw new ResourceNotFoundException("Project", id);

    public async Task<ProjectResponse> UpdateAsync(Guid id, UpdateProjectRequest request, CancellationToken ct)
    {
        // Tracked, so the UPDATE carries WHERE RowVersion = @original.
        var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new ResourceNotFoundException("Project", id);

        var now = clock.GetUtcNow();

        if (request.Name is not null)
        {
            project.Rename(request.Name, now);
        }

        if (request.BudgetUsd is { } budget)
        {
            project.ChangeBudget(budget, now);
            await WarnIfBudgetBelowApprovedActualsAsync(project, ct);
        }

        if (request.Status is { } status)
        {
            var hasPending = status == ProjectStatus.Completed && await HasPendingSubmissionsAsync(id, ct);
            project.ChangeStatus(status, hasPending, now);
        }

        await db.SaveChangesAsync(ct);
        return ToResponse(project);
    }

    private async Task<bool> HasPendingSubmissionsAsync(Guid projectId, CancellationToken ct) =>
        await db.Timesheets.AnyAsync(t => t.ProjectId == projectId && t.Status == SubmissionStatus.Pending, ct)
        || await db.Expenses.AnyAsync(e => e.ProjectId == projectId && e.Status == SubmissionStatus.Pending, ct);

    private async Task WarnIfBudgetBelowApprovedActualsAsync(Project project, CancellationToken ct)
    {
        var approvedLabor = await db.Timesheets
            .Where(t => t.ProjectId == project.Id && t.Status == SubmissionStatus.Approved)
            .SumAsync(t => (decimal?)t.LaborCostUsd, ct) ?? 0m;
        var approvedExpenses = await db.Expenses
            .Where(e => e.ProjectId == project.Id && e.Status == SubmissionStatus.Approved)
            .SumAsync(e => (decimal?)e.AmountUsd, ct) ?? 0m;

        var approvedActual = approvedLabor + approvedExpenses;
        if (project.BudgetUsd < approvedActual)
        {
            logger.LogWarning(
                "Project {ProjectId} budget set to {BudgetUsd}, below approved actuals {ApprovedActualUsd}",
                project.Id,
                project.BudgetUsd,
                approvedActual);
        }
    }
}
