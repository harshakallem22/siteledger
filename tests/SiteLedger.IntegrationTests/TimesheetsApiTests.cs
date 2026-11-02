using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using SiteLedger.Application.Contracts.Common;
using SiteLedger.Application.Contracts.Timesheets;
using SiteLedger.Domain.Enums;
using SiteLedger.IntegrationTests.Fixtures;

namespace SiteLedger.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class TimesheetsApiTests(SiteLedgerApiFactory factory) : IAsyncLifetime
{
    private static readonly DateOnly Yesterday = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);
    private readonly HttpClient _client = factory.CreateTrustedClient();

    public Task InitializeAsync() => factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Submit_SnapshotsStoredEmployeeRate_AndIgnoresClientSuppliedRate()
    {
        var project = await _client.CreateProjectAsync("CON-2026-001");
        var employee = await factory.AddEmployeeAsync(hourlyRateUsd: 55.55m);

        var response = await _client.PostAsJsonAsync(Url(project.Id), new
        {
            employeeId = employee.Id,
            workDate = Yesterday,
            hours = 7.25m,
            hourlyRateSnapshotUsd = 999.99m, // must be ignored: the server owns the rate
            status = "Approved",             // must be ignored: new submissions are always Pending
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.ReadAsync<TimesheetResponse>();
        Assert.Equal(55.55m, body.HourlyRateSnapshotUsd);
        Assert.Equal(402.74m, body.LaborCostUsd);
        Assert.Equal(SubmissionStatus.Pending, body.Status);

        var row = await factory.QueryDbAsync(db => db.Timesheets.AsNoTracking().SingleAsync(t => t.Id == body.Id));
        Assert.Equal(55.55m, row.HourlyRateSnapshotUsd);
        Assert.Equal(402.74m, row.LaborCostUsd);
        Assert.Equal(SubmissionStatus.Pending, row.Status);
    }

    [Fact]
    public async Task Submit_SameEmployeeProjectAndDay_Returns409_ButOtherDaysAreAllowed()
    {
        var project = await _client.CreateProjectAsync("CON-2026-001");
        var employee = await factory.AddEmployeeAsync(hourlyRateUsd: 40m);

        var first = await _client.PostAsJsonAsync(Url(project.Id), new { employeeId = employee.Id, workDate = Yesterday, hours = 8m });
        var duplicate = await _client.PostAsJsonAsync(Url(project.Id), new { employeeId = employee.Id, workDate = Yesterday, hours = 4m });
        var nextDay = await _client.PostAsJsonAsync(Url(project.Id), new { employeeId = employee.Id, workDate = Yesterday.AddDays(-1), hours = 4m });

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal(HttpStatusCode.Created, nextDay.StatusCode);
        Assert.Equal(2, await factory.QueryDbAsync(db => db.Timesheets.CountAsync()));
    }

    [Fact]
    public async Task Submit_ReturnsCorrectErrors_ForBadInput_MissingReferences_AndInactiveTargets()
    {
        var active = await _client.CreateProjectAsync("CON-ACTIVE");
        var draft = await _client.CreateProjectAsync("CON-DRAFT", status: "Draft");
        var employee = await factory.AddEmployeeAsync(hourlyRateUsd: 40m);
        var formerEmployee = await factory.AddEmployeeAsync(hourlyRateUsd: 40m, isActive: false);
        var tomorrow = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1);

        async Task<HttpStatusCode> Post(Guid projectId, object body) => (await _client.PostAsJsonAsync(Url(projectId), body)).StatusCode;

        Assert.Equal(HttpStatusCode.BadRequest, await Post(active.Id, new { employeeId = employee.Id, workDate = tomorrow, hours = 8m }));
        Assert.Equal(HttpStatusCode.BadRequest, await Post(active.Id, new { employeeId = employee.Id, workDate = Yesterday, hours = 24.5m }));
        Assert.Equal(HttpStatusCode.BadRequest, await Post(active.Id, new { employeeId = employee.Id, hours = 8m }));
        Assert.Equal(HttpStatusCode.NotFound, await Post(Guid.NewGuid(), new { employeeId = employee.Id, workDate = Yesterday, hours = 8m }));
        Assert.Equal(HttpStatusCode.NotFound, await Post(active.Id, new { employeeId = Guid.NewGuid(), workDate = Yesterday, hours = 8m }));
        Assert.Equal(HttpStatusCode.Conflict, await Post(draft.Id, new { employeeId = employee.Id, workDate = Yesterday, hours = 8m }));
        Assert.Equal(HttpStatusCode.Conflict, await Post(active.Id, new { employeeId = formerEmployee.Id, workDate = Yesterday, hours = 8m }));

        Assert.Equal(0, await factory.QueryDbAsync(db => db.Timesheets.CountAsync()));
    }

    [Fact]
    public async Task List_ReturnsOnlyThatProjectsTimesheets_FilteredAndPaged()
    {
        var project = await _client.CreateProjectAsync("CON-A");
        var otherProject = await _client.CreateProjectAsync("CON-B");
        var employee = await factory.AddEmployeeAsync(hourlyRateUsd: 50m);
        for (var day = 1; day <= 3; day++)
        {
            await _client.PostAsJsonAsync(Url(project.Id), new { employeeId = employee.Id, workDate = Yesterday.AddDays(-day), hours = 8m });
        }

        await _client.PostAsJsonAsync(Url(otherProject.Id), new { employeeId = employee.Id, workDate = Yesterday, hours = 8m });

        var page1 = await (await _client.GetAsync($"{Url(project.Id)}?status=Pending&page=1&pageSize=2")).ReadAsync<PagedResponse<TimesheetResponse>>();
        var approved = await (await _client.GetAsync($"{Url(project.Id)}?status=Approved")).ReadAsync<PagedResponse<TimesheetResponse>>();
        var unknownProject = await _client.GetAsync(Url(Guid.NewGuid()));

        Assert.Equal(3, page1.TotalCount);
        Assert.Equal(2, page1.Items.Count);
        Assert.All(page1.Items, t => Assert.Equal(project.Id, t.ProjectId));
        Assert.Equal(0, approved.TotalCount);
        Assert.Equal(HttpStatusCode.NotFound, unknownProject.StatusCode);
    }

    private static string Url(Guid projectId) => $"/api/v1/projects/{projectId}/timesheets";
}
