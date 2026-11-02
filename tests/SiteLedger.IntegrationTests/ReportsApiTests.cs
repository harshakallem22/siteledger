using System.Net;
using System.Net.Http.Json;
using SiteLedger.Application.Contracts.Reports;
using SiteLedger.IntegrationTests.Fixtures;

namespace SiteLedger.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class ReportsApiTests(SiteLedgerApiFactory factory) : IAsyncLifetime
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);
    private readonly HttpClient _client = factory.CreateTrustedClient();

    public Task InitializeAsync() => factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact] // I12: the spec's representative story, plus pending and rejected noise
    public async Task ProjectReport_CountsApprovedOnly_ShowsPendingSeparately_AndIgnoresRejected()
    {
        var project = await CreateProjectAsync("CON-2026-001", budget: 250_000.00m);
        var employee = await factory.AddEmployeeAsync(hourlyRateUsd: 55.00m);

        await DecideTimesheetAsync(project.Id, employee.Id, Today.AddDays(-1), hours: 8m, "approve");  // $440 approved
        await DecideTimesheetAsync(project.Id, employee.Id, Today.AddDays(-2), hours: 4m, decision: null); // $220 pending
        await DecideTimesheetAsync(project.Id, employee.Id, Today.AddDays(-3), hours: 2m, "reject");   // $110 rejected
        await DecideExpenseAsync(project.Id, 1_250.00m, "approve");
        await DecideExpenseAsync(project.Id, 300.00m, decision: null);
        await DecideExpenseAsync(project.Id, 999.00m, "reject");

        var report = await GetProjectReportAsync(project.Id);

        Assert.Equal(250_000.00m, report.BudgetUsd);
        Assert.Equal(440.00m, report.ApprovedLaborActualUsd);
        Assert.Equal(1_250.00m, report.ApprovedExpenseActualUsd);
        Assert.Equal(1_690.00m, report.TotalActualUsd);
        Assert.Equal(220.00m, report.PendingLaborUsd);
        Assert.Equal(300.00m, report.PendingExpenseUsd);
        Assert.Equal(248_310.00m, report.RemainingBudgetUsd);
        Assert.Equal(0.68m, report.BudgetUsedPct);
        Assert.False(report.IsOverBudget);
    }

    [Fact]
    public async Task ProjectReport_WithNoSubmissions_IsAllZeros_AndUnknownProjectIs404()
    {
        var project = await CreateProjectAsync("CON-EMPTY", budget: 5_000.00m);

        var report = await GetProjectReportAsync(project.Id);

        Assert.Equal(0m, report.TotalActualUsd);
        Assert.Equal(0m, report.PendingLaborUsd + report.PendingExpenseUsd);
        Assert.Equal(5_000.00m, report.RemainingBudgetUsd);
        Assert.Equal(0m, report.BudgetUsedPct);
        Assert.False(report.IsOverBudget);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/v1/projects/{Guid.NewGuid()}/reports/budget-vs-actual")).StatusCode);
    }

    [Fact]
    public async Task ProjectReport_AfterBudgetIsCutBelowActuals_ShowsNegativeRemainingAndOverBudget()
    {
        var project = await CreateProjectAsync("CON-OVER", budget: 2_000.00m);
        await DecideExpenseAsync(project.Id, 1_250.00m, "approve");
        var cut = await _client.PatchAsJsonAsync($"/api/v1/projects/{project.Id}", new { budgetUsd = 1_000.00m }); // allowed by design

        var report = await GetProjectReportAsync(project.Id);

        Assert.Equal(HttpStatusCode.OK, cut.StatusCode);
        Assert.Equal(-250.00m, report.RemainingBudgetUsd);
        Assert.Equal(125.00m, report.BudgetUsedPct);
        Assert.True(report.IsOverBudget);
    }

    [Fact] // I13
    public async Task Portfolio_TotalsCoverAllMatchingProjects_NotJustThePage_AndPercentIsWeighted()
    {
        var a = await CreateProjectAsync("CON-A", budget: 1_000.00m);
        var b = await CreateProjectAsync("CON-B", budget: 9_000.00m);
        var c = await CreateProjectAsync("CON-C", budget: 10_000.00m);
        await CreateProjectAsync("CON-D", budget: 5_000.00m, status: "Draft");
        var employee = await factory.AddEmployeeAsync(hourlyRateUsd: 50.00m);

        await DecideExpenseAsync(a.Id, 1_250.00m, "approve");                                     // A: $1,250 of $1,000 → over budget
        await DecideExpenseAsync(b.Id, 700.00m, decision: null);                                  // B: pending only → $0 actual
        await DecideTimesheetAsync(c.Id, employee.Id, Today.AddDays(-1), hours: 8m, "approve");  // C: $400 labor
        await DecideExpenseAsync(c.Id, 100.00m, "approve");                                       //    + $100 expense = $500
        await DecideExpenseAsync(c.Id, 5_000.00m, "reject");                                      //    rejected: ignored

        var page1 = await GetPortfolioAsync("?status=Active&page=1&pageSize=2");
        var page2 = await GetPortfolioAsync("?status=Active&page=2&pageSize=2");

        // All 3 Active projects, identical on both pages.
        foreach (var page in new[] { page1, page2 })
        {
            Assert.Equal(3, page.TotalProjectsMatchingFilter);
            Assert.Equal(20_000.00m, page.PortfolioBudgetUsd);
            Assert.Equal(1_750.00m, page.PortfolioApprovedActualUsd);
            Assert.Equal(18_250.00m, page.PortfolioRemainingUsd);
            Assert.Equal(8.75m, page.PortfolioBudgetUsedPct); // weighted; the average of 125%, 0%, 5% would be 43.33
            Assert.Equal(1, page.OverBudgetProjectCount);
        }

        Assert.Equal(["CON-A", "CON-B"], page1.Items.Select(i => i.ProjectCode));
        var itemA = page1.Items[0];
        Assert.Equal(1_250.00m, itemA.ApprovedActualUsd);
        Assert.Equal(-250.00m, itemA.RemainingBudgetUsd);
        Assert.True(itemA.IsOverBudget);
        Assert.Equal(0m, page1.Items[1].ApprovedActualUsd);                  // project with no approved items still listed
        Assert.Equal(500.00m, Assert.Single(page2.Items).ApprovedActualUsd); // CON-C

        var all = await GetPortfolioAsync(string.Empty);                     // no filter: includes the Draft project
        Assert.Equal(4, all.TotalProjectsMatchingFilter);
        Assert.Equal(25_000.00m, all.PortfolioBudgetUsd);
    }

    [Fact]
    public async Task Portfolio_WithNoMatchingProjects_IsAllZeros_AndBadPagingIs400()
    {
        await CreateProjectAsync("CON-A", budget: 1_000.00m);

        var completed = await GetPortfolioAsync("?status=Completed");

        Assert.Equal(0, completed.TotalProjectsMatchingFilter);
        Assert.Equal(0m, completed.PortfolioBudgetUsd);
        Assert.Equal(0m, completed.PortfolioBudgetUsedPct);
        Assert.Empty(completed.Items);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.GetAsync("/api/v1/reports/portfolio-summary?pageSize=101")).StatusCode);
    }

    private async Task<Application.Contracts.Projects.ProjectResponse> CreateProjectAsync(string code, decimal budget, string status = "Active")
    {
        var response = await _client.PostAsJsonAsync("/api/v1/projects", new { projectCode = code, name = $"Project {code}", budgetUsd = budget, status });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.ReadAsync<Application.Contracts.Projects.ProjectResponse>();
    }

    private async Task DecideTimesheetAsync(Guid projectId, Guid employeeId, DateOnly day, decimal hours, string? decision)
    {
        var timesheet = await _client.SubmitTimesheetAsync(projectId, employeeId, day, hours);
        await DecideAsync($"/api/v1/timesheets/{timesheet.Id}", decision);
    }

    private async Task DecideExpenseAsync(Guid projectId, decimal amount, string? decision)
    {
        var expense = await _client.SubmitExpenseAsync(projectId, amount);
        await DecideAsync($"/api/v1/expenses/{expense.Id}", decision);
    }

    private async Task DecideAsync(string baseUrl, string? decision)
    {
        if (decision is null)
        {
            return; // leave Pending
        }

        var response = decision == "approve"
            ? await _client.PostAsync($"{baseUrl}/approve", content: null)
            : await _client.PostAsJsonAsync($"{baseUrl}/reject", new { reason = "Not supported by documentation" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task<ProjectBudgetReportResponse> GetProjectReportAsync(Guid projectId)
    {
        var response = await _client.GetAsync($"/api/v1/projects/{projectId}/reports/budget-vs-actual");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.ReadAsync<ProjectBudgetReportResponse>();
    }

    private async Task<PortfolioReportResponse> GetPortfolioAsync(string queryString)
    {
        var response = await _client.GetAsync($"/api/v1/reports/portfolio-summary{queryString}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.ReadAsync<PortfolioReportResponse>();
    }
}
