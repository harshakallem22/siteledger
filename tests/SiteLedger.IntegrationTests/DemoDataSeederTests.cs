using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SiteLedger.Domain.Enums;
using SiteLedger.Infrastructure.Persistence;
using SiteLedger.Infrastructure.Seeding;
using SiteLedger.IntegrationTests.Fixtures;

namespace SiteLedger.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class DemoDataSeederTests(SiteLedgerApiFactory factory) : IAsyncLifetime
{
    // Seeding ~17,600 rows is slow and these tests only read, so seed once per run (other classes reset first).
    private static readonly SemaphoreSlim SeedGate = new(1, 1);
    private static bool _seeded;

    public async Task InitializeAsync()
    {
        await SeedGate.WaitAsync();
        try
        {
            if (!_seeded)
            {
                await factory.ResetDatabaseAsync();
                await SeedAsync();
                _seeded = true;
            }
        }
        finally
        {
            SeedGate.Release();
        }
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Seed_Inserts10000FinancialLines_WithSpecDistribution_AndIsIdempotent()
    {
        var counts = await CountsAsync();
        Assert.Equal((200, 100, 6_000, 4_000), (counts.Employees, counts.Projects, counts.Timesheets, counts.Expenses));

        // ~60/25/15 % per table (completed projects nudge Pending → Approved).
        foreach (var (table, byStatus, total) in new[] { ("Timesheets", counts.TimesheetsByStatus, 6_000), ("Expenses", counts.ExpensesByStatus, 4_000) })
        {
            AssertShare(table, "Approved", byStatus[SubmissionStatus.Approved], total, 0.55, 0.67);
            AssertShare(table, "Pending", byStatus[SubmissionStatus.Pending], total, 0.18, 0.28);
            AssertShare(table, "Rejected", byStatus[SubmissionStatus.Rejected], total, 0.11, 0.19);
        }

        // Second run inserts nothing.
        await SeedAsync();
        var again = await CountsAsync();
        Assert.Equal((counts.Employees, counts.Projects, counts.Timesheets, counts.Expenses, counts.Audits),
                     (again.Employees, again.Projects, again.Timesheets, again.Expenses, again.Audits));
    }

    [Fact]
    public async Task Seed_EveryDecisionHasExactlyOneMatchingAudit_AndCompletedProjectsHaveNoPending()
    {
        var (decided, audits, mismatched, pendingOnCompleted) = await factory.QueryDbAsync(async db =>
        {
            var decidedRows = await db.Timesheets.CountAsync(t => t.Status != SubmissionStatus.Pending)
                + await db.Expenses.CountAsync(e => e.Status != SubmissionStatus.Pending);
            var auditRows = await db.ApprovalAudits.CountAsync();

            // "Matches" = same action as the row's status, same time and actor.
            var mismatchedTimesheets = await db.ApprovalAudits.CountAsync(a => a.SubmissionType == SubmissionType.Timesheet &&
                !db.Timesheets.Any(t => t.Id == a.SubmissionId && t.DecidedAtUtc == a.OccurredAtUtc && t.DecidedBySubject == a.ActorSubject &&
                    ((a.Action == AuditAction.Approved && t.Status == SubmissionStatus.Approved) || (a.Action == AuditAction.Rejected && t.Status == SubmissionStatus.Rejected))));
            var mismatchedExpenses = await db.ApprovalAudits.CountAsync(a => a.SubmissionType == SubmissionType.Expense &&
                !db.Expenses.Any(e => e.Id == a.SubmissionId && e.DecidedAtUtc == a.OccurredAtUtc && e.DecidedBySubject == a.ActorSubject &&
                    ((a.Action == AuditAction.Approved && e.Status == SubmissionStatus.Approved) || (a.Action == AuditAction.Rejected && e.Status == SubmissionStatus.Rejected))));

            var completedIds = db.Projects.Where(p => p.Status == ProjectStatus.Completed).Select(p => p.Id);
            var pending = await db.Timesheets.CountAsync(t => completedIds.Contains(t.ProjectId) && t.Status == SubmissionStatus.Pending)
                + await db.Expenses.CountAsync(e => completedIds.Contains(e.ProjectId) && e.Status == SubmissionStatus.Pending);

            return (decidedRows, auditRows, mismatchedTimesheets + mismatchedExpenses, pending);
        });

        Assert.Equal(decided, audits);
        Assert.Equal(0, mismatched);
        Assert.Equal(0, pendingOnCompleted);
    }

    [Fact]
    public async Task Seed_ProducesOverBudgetAndZeroSpendProjects_VisibleThroughThePortfolioReport()
    {
        var report = await (await factory.CreateTrustedClient().GetAsync("/api/v1/reports/portfolio-summary?page=1&pageSize=100"))
            .ReadAsync<Application.Contracts.Reports.PortfolioReportResponse>();

        Assert.Equal(100, report.TotalProjectsMatchingFilter);
        Assert.True(report.OverBudgetProjectCount >= 3, $"over-budget projects: {report.OverBudgetProjectCount}");
        foreach (var number in DemoDataSeeder.OverBudgetProjects)
        {
            Assert.True(report.Items.Single(i => i.ProjectCode == $"SL-2026-{number:D3}").IsOverBudget);
        }

        Assert.True(report.Items.Count(i => i.ApprovedActualUsd == 0) >= 5, "expected Draft projects with zero actual spend");
    }

    [Fact]
    public async Task Build_IsDeterministic_AndMatchesWhatWasStored()
    {
        var first = Fingerprint(BuildInMemory());
        var second = Fingerprint(BuildInMemory());
        Assert.Equal(first, second);

        await SeedAsync(); // already seeded: proves a re-run doesn't change what's stored
        var stored = await factory.QueryDbAsync(async db => (
            Labor: await db.Timesheets.SumAsync(t => t.LaborCostUsd),
            Expenses: await db.Expenses.SumAsync(e => e.AmountUsd),
            Project1: await db.Projects.Where(p => p.Id == DemoDataSeeder.StableId('a', 1)).Select(p => p.ProjectCode).SingleAsync(),
            Timesheet6000: await db.Timesheets.AnyAsync(t => t.Id == DemoDataSeeder.StableId('b', 6_000))));

        Assert.Equal(first.Labor, stored.Labor);
        Assert.Equal(first.Expenses, stored.Expenses);
        Assert.Equal("SL-2026-001", stored.Project1);
        Assert.True(stored.Timesheet6000);
    }

    private DemoDataSeeder.SeedData BuildInMemory()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SiteLedgerDbContext>(); // used only to set Ids; no SQL is sent
        return DemoDataSeeder.BuildFinancialData(DemoDataSeeder.BuildEmployees(), (entity, id) => db.Entry(entity).Property("Id").CurrentValue = id);
    }

    private static (decimal Labor, decimal Expenses, string Ids, string Statuses) Fingerprint(DemoDataSeeder.SeedData data) => (
        data.Timesheets.Sum(t => t.LaborCostUsd),
        data.Expenses.Sum(e => e.AmountUsd),
        string.Join(",", data.Timesheets.Take(5).Select(t => t.Id).Concat(data.Expenses.Take(5).Select(e => e.Id))),
        string.Concat(data.Timesheets.Select(t => (int)t.Status)) + string.Concat(data.Expenses.Select(e => (int)e.Status)));

    private Task SeedAsync() => factory.QueryDbAsync(async db =>
    {
        await DemoDataSeeder.SeedAsync(db, factory.Services.GetRequiredService<ILogger<DemoDataSeederTests>>());
        return true;
    });

    private Task<(int Employees, int Projects, int Timesheets, int Expenses, int Audits,
        Dictionary<SubmissionStatus, int> TimesheetsByStatus, Dictionary<SubmissionStatus, int> ExpensesByStatus)> CountsAsync() =>
        factory.QueryDbAsync(async db => (
            await db.Employees.CountAsync(),
            await db.Projects.CountAsync(p => p.ProjectCode.StartsWith(DemoDataSeeder.ProjectCodePrefix)),
            await db.Timesheets.CountAsync(),
            await db.Expenses.CountAsync(),
            await db.ApprovalAudits.CountAsync(),
            await db.Timesheets.GroupBy(t => t.Status).ToDictionaryAsync(g => g.Key, g => g.Count()),
            await db.Expenses.GroupBy(e => e.Status).ToDictionaryAsync(g => g.Key, g => g.Count())));

    private static void AssertShare(string table, string status, int count, int total, double min, double max)
    {
        var share = (double)count / total;
        Assert.True(share >= min && share <= max, $"{table} {status}: {count}/{total} = {share:P1}, expected {min:P0}–{max:P0}");
    }
}
