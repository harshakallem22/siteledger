using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using SiteLedger.Application.Contracts.Common;
using SiteLedger.Application.Contracts.Projects;
using SiteLedger.Domain.Enums;
using SiteLedger.IntegrationTests.Fixtures;

namespace SiteLedger.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class ProjectsApiTests(SiteLedgerApiFactory factory) : IAsyncLifetime
{
    private const string Route = "/api/v1/projects";
    private readonly HttpClient _client = factory.CreateTrustedClient();

    public Task InitializeAsync() => factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Create_ThenGet_ReturnsProjectStoredInSqlServer()
    {
        var create = await _client.PostAsJsonAsync(Route, new
        {
            projectCode = "con-2026-001",
            name = "Fremont Equipment Bay Upgrade",
            budgetUsd = 250000.00m,
            status = "Active",
        });

        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var created = await create.ReadAsync<ProjectResponse>();
        Assert.Equal("CON-2026-001", created.ProjectCode);
        Assert.Equal($"{Route}/{created.Id}", create.Headers.Location?.AbsolutePath);

        var get = await _client.GetAsync(create.Headers.Location);
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        Assert.Equal(created, await get.ReadAsync<ProjectResponse>());

        var row = await factory.QueryDbAsync(db => db.Projects.AsNoTracking().SingleAsync(p => p.Id == created.Id));
        Assert.Equal(250000.00m, row.BudgetUsd);
        Assert.Equal(ProjectStatus.Active, row.Status);
        Assert.NotEmpty(row.RowVersion);
    }

    [Fact]
    public async Task Create_WithDuplicateCodeInAnyCase_Returns409AndKeepsOneRow()
    {
        await _client.CreateProjectAsync("CON-2026-001");

        var duplicate = await _client.PostAsJsonAsync(Route, new { projectCode = "con-2026-001", name = "Duplicate", budgetUsd = 10m });

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal("application/problem+json", duplicate.Content.Headers.ContentType?.MediaType);
        Assert.Equal(1, await factory.QueryDbAsync(db => db.Projects.CountAsync()));
    }

    [Fact]
    public async Task List_FiltersByStatus_PaginatesInSql_AndSortsByCode()
    {
        // Inserted out of order on purpose.
        await _client.CreateProjectAsync("CON-C", status: "Active");
        await _client.CreateProjectAsync("CON-A", status: "Active");
        await _client.CreateProjectAsync("CON-D", status: "Draft");
        await _client.CreateProjectAsync("CON-B", status: "Active");
        await _client.CreateProjectAsync("CON-E", status: "Draft");

        var page1 = await (await _client.GetAsync($"{Route}?status=Active&page=1&pageSize=2")).ReadAsync<PagedResponse<ProjectResponse>>();
        var page2 = await (await _client.GetAsync($"{Route}?status=Active&page=2&pageSize=2")).ReadAsync<PagedResponse<ProjectResponse>>();

        Assert.Equal(3, page1.TotalCount);
        Assert.Equal(["CON-A", "CON-B"], page1.Items.Select(p => p.ProjectCode));
        Assert.Equal(3, page2.TotalCount);
        Assert.Equal(["CON-C"], page2.Items.Select(p => p.ProjectCode));

        var invalid = await _client.GetAsync($"{Route}?pageSize=101");
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    [Fact]
    public async Task Patch_UpdatesBudget_AndEnforcesStatusLifecycle()
    {
        var project = await _client.CreateProjectAsync("CON-2026-001");
        var url = $"{Route}/{project.Id}";

        var budget = await _client.PatchAsJsonAsync(url, new { budgetUsd = 260000.00m });
        Assert.Equal(HttpStatusCode.OK, budget.StatusCode);
        Assert.Equal(260000.00m, (await budget.ReadAsync<ProjectResponse>()).BudgetUsd);

        var backToDraft = await _client.PatchAsJsonAsync(url, new { status = "Draft" });
        Assert.Equal(HttpStatusCode.Conflict, backToDraft.StatusCode);

        var complete = await _client.PatchAsJsonAsync(url, new { status = "Completed" });
        Assert.Equal(HttpStatusCode.OK, complete.StatusCode);

        var renameCompleted = await _client.PatchAsJsonAsync(url, new { name = "Too late" });
        Assert.Equal(HttpStatusCode.Conflict, renameCompleted.StatusCode);

        var missing = await _client.PatchAsJsonAsync($"{Route}/{Guid.NewGuid()}", new { name = "Nobody" });
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

        var row = await factory.QueryDbAsync(db => db.Projects.AsNoTracking().SingleAsync(p => p.Id == project.Id));
        Assert.Equal(260000.00m, row.BudgetUsd);
        Assert.Equal(ProjectStatus.Completed, row.Status);
        Assert.Equal("Fremont Equipment Bay Upgrade", row.Name);
    }
}
