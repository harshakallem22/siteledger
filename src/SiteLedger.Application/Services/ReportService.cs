using Microsoft.EntityFrameworkCore;
using SiteLedger.Application.Abstractions;
using SiteLedger.Application.Contracts.Common;
using SiteLedger.Application.Contracts.Reports;
using SiteLedger.Application.Exceptions;
using SiteLedger.Domain.Enums;
using SiteLedger.Domain.Rules;

namespace SiteLedger.Application.Services;

// All sums run in SQL Server and are never cached, so benchmarks measure real SQL work.
public sealed class ReportService(ISiteLedgerDbContext db, TimeProvider clock) : IReportService
{
    // 0.00m (not 0m) so JSON always shows cents.
    private const decimal ZeroUsd = 0.00m;

    // 3 queries: header, labor totals by status, expense totals by status.
    public async Task<ProjectBudgetReportResponse> GetProjectReportAsync(Guid projectId, CancellationToken ct)
    {
        var project = await db.Projects.AsNoTracking()
            .Where(p => p.Id == projectId)
            .Select(p => new { p.Id, p.ProjectCode, p.Name, p.BudgetUsd })
            .FirstOrDefaultAsync(ct)
            ?? throw new ResourceNotFoundException("Project", projectId);

        var labor = await db.Timesheets.AsNoTracking()
            .Where(t => t.ProjectId == projectId && t.Status != SubmissionStatus.Rejected)
            .GroupBy(t => t.Status)
            .Select(g => new { Status = g.Key, Total = g.Sum(t => t.LaborCostUsd) })
            .ToDictionaryAsync(x => x.Status, x => x.Total, ct);

        var expenses = await db.Expenses.AsNoTracking()
            .Where(e => e.ProjectId == projectId && e.Status != SubmissionStatus.Rejected)
            .GroupBy(e => e.Status)
            .Select(g => new { Status = g.Key, Total = g.Sum(e => e.AmountUsd) })
            .ToDictionaryAsync(x => x.Status, x => x.Total, ct);

        var approvedLabor = labor.GetValueOrDefault(SubmissionStatus.Approved, ZeroUsd);
        var approvedExpenses = expenses.GetValueOrDefault(SubmissionStatus.Approved, ZeroUsd);
        var totalActual = approvedLabor + approvedExpenses;

        return new ProjectBudgetReportResponse(
            project.Id,
            project.ProjectCode,
            project.Name,
            project.BudgetUsd,
            approvedLabor,
            approvedExpenses,
            totalActual,
            labor.GetValueOrDefault(SubmissionStatus.Pending, ZeroUsd),
            expenses.GetValueOrDefault(SubmissionStatus.Pending, ZeroUsd),
            project.BudgetUsd - totalActual,
            BudgetMath.UsedPercent(totalActual, project.BudgetUsd),
            BudgetMath.IsOverBudget(totalActual, project.BudgetUsd),
            clock.GetUtcNow());
    }

    // 2 queries regardless of project count (no N+1): totals over all matches, then one page.
    public async Task<PortfolioReportResponse> GetPortfolioReportAsync(PortfolioQuery query, CancellationToken ct)
    {
        Paging.Validate(query.Page, query.PageSize);

        var projects = db.Projects.AsNoTracking();
        if (query.Status is { } status)
        {
            projects = projects.Where(p => p.Status == status);
        }

        var approvedLaborByProject = db.Timesheets
            .Where(t => t.Status == SubmissionStatus.Approved)
            .GroupBy(t => t.ProjectId)
            .Select(g => new { ProjectId = g.Key, Total = g.Sum(t => t.LaborCostUsd) });

        var approvedExpensesByProject = db.Expenses
            .Where(e => e.Status == SubmissionStatus.Approved)
            .GroupBy(e => e.ProjectId)
            .Select(g => new { ProjectId = g.Key, Total = g.Sum(e => e.AmountUsd) });

        // LEFT JOINs keep projects with no approved items (actual 0).
        var perProject =
            from p in projects
            join l in approvedLaborByProject on p.Id equals l.ProjectId into laborJoin
            from l in laborJoin.DefaultIfEmpty()
            join e in approvedExpensesByProject on p.Id equals e.ProjectId into expenseJoin
            from e in expenseJoin.DefaultIfEmpty()
            select new
            {
                p.Id,
                p.ProjectCode,
                p.BudgetUsd,
                ActualUsd = ((decimal?)l.Total ?? 0m) + ((decimal?)e.Total ?? 0m),
            };

        var totals = await perProject
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Count = g.Count(),
                BudgetUsd = g.Sum(x => x.BudgetUsd),
                ActualUsd = g.Sum(x => x.ActualUsd),
                OverBudgetCount = g.Count(x => x.ActualUsd > x.BudgetUsd),
            })
            .FirstOrDefaultAsync(ct);

        // Same stable sort as the project list.
        var page = await perProject
            .OrderBy(x => x.ProjectCode)
            .ThenBy(x => x.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(ct);

        var portfolioBudget = totals?.BudgetUsd ?? ZeroUsd;
        var portfolioActual = totals?.ActualUsd ?? ZeroUsd;

        return new PortfolioReportResponse(
            totals?.Count ?? 0,
            portfolioBudget,
            portfolioActual,
            portfolioBudget - portfolioActual,
            BudgetMath.UsedPercent(portfolioActual, portfolioBudget),
            totals?.OverBudgetCount ?? 0,
            query.Page,
            query.PageSize,
            page.Select(x => new PortfolioProjectItem(
                    x.Id,
                    x.ProjectCode,
                    x.BudgetUsd,
                    x.ActualUsd,
                    x.BudgetUsd - x.ActualUsd,
                    BudgetMath.IsOverBudget(x.ActualUsd, x.BudgetUsd)))
                .ToList());
    }
}
