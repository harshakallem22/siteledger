using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using SiteLedger.Application.Contracts.Common;
using SiteLedger.Application.Contracts.Expenses;
using SiteLedger.Domain.Enums;
using SiteLedger.IntegrationTests.Fixtures;

namespace SiteLedger.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class ExpensesApiTests(SiteLedgerApiFactory factory) : IAsyncLifetime
{
    private static readonly DateOnly Yesterday = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);
    private readonly HttpClient _client = factory.CreateTrustedClient();

    public Task InitializeAsync() => factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact] // I09 (happy path)
    public async Task Submit_Valid_Returns201_AndStoresPendingExpenseInSql()
    {
        var project = await _client.CreateProjectAsync("CON-2026-001");

        var response = await _client.PostAsJsonAsync(Url(project.Id), new
        {
            description = "Electrical conduit and mounting hardware",
            category = "Materials",
            amountUsd = 1250.00m,
            expenseDate = Yesterday,
            externalReference = "PO-1873",
            status = "Approved", // must be ignored: server owns status
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.ReadAsync<ExpenseResponse>();
        Assert.Equal(SubmissionStatus.Pending, body.Status);
        Assert.Equal(ExpenseCategory.Materials, body.Category);

        var row = await factory.QueryDbAsync(db => db.Expenses.AsNoTracking().SingleAsync(e => e.Id == body.Id));
        Assert.Equal(project.Id, row.ProjectId);
        Assert.Equal(1250.00m, row.AmountUsd);
        Assert.Equal("PO-1873", row.ExternalReference);
        Assert.Equal(SubmissionStatus.Pending, row.Status);
        Assert.NotEmpty(row.RowVersion);
    }

    [Fact] // I09 (validation)
    public async Task Submit_Invalid_Returns400_404Or409_AndWritesNothing()
    {
        var active = await _client.CreateProjectAsync("CON-ACTIVE");
        var draft = await _client.CreateProjectAsync("CON-DRAFT", status: "Draft");
        var tomorrow = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1);
        object Valid(string? category = "Materials", decimal amount = 100m, DateOnly? date = null, string description = "Valid description") =>
            new { description, category, amountUsd = amount, expenseDate = date ?? Yesterday };

        async Task<HttpStatusCode> Post(Guid projectId, object body) => (await _client.PostAsJsonAsync(Url(projectId), body)).StatusCode;

        Assert.Equal(HttpStatusCode.BadRequest, await Post(active.Id, Valid(category: "Food")));          // not a category
        Assert.Equal(HttpStatusCode.BadRequest, await Post(active.Id, Valid(amount: 0m)));
        Assert.Equal(HttpStatusCode.BadRequest, await Post(active.Id, Valid(amount: 1_000_000.01m)));     // above configured max
        Assert.Equal(HttpStatusCode.BadRequest, await Post(active.Id, Valid(date: tomorrow)));
        Assert.Equal(HttpStatusCode.BadRequest, await Post(active.Id, Valid(description: "ab")));
        Assert.Equal(HttpStatusCode.BadRequest, await Post(active.Id, Valid(category: null)));            // missing category
        Assert.Equal(HttpStatusCode.NotFound, await Post(Guid.NewGuid(), Valid()));
        Assert.Equal(HttpStatusCode.Conflict, await Post(draft.Id, Valid()));

        Assert.Equal(0, await factory.QueryDbAsync(db => db.Expenses.CountAsync()));
    }

    [Fact]
    public async Task Submit_SameExternalReferenceTwice_IsAllowed()
    {
        var project = await _client.CreateProjectAsync("CON-2026-001");
        var body = new { description = "Partial delivery", category = "Materials", amountUsd = 500m, expenseDate = Yesterday, externalReference = "PO-1873" };

        var first = await _client.PostAsJsonAsync(Url(project.Id), body);
        var second = await _client.PostAsJsonAsync(Url(project.Id), body);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
    }

    [Fact]
    public async Task List_ReturnsOnlyThatProjectsExpenses_NewestExpenseDateFirst_FilteredAndPaged()
    {
        var project = await _client.CreateProjectAsync("CON-A");
        var other = await _client.CreateProjectAsync("CON-B");
        foreach (var daysAgo in new[] { 5, 1, 3 })
        {
            await _client.PostAsJsonAsync(Url(project.Id), new { description = $"Item {daysAgo}", category = "Equipment", amountUsd = 10m, expenseDate = Yesterday.AddDays(-daysAgo) });
        }

        await _client.PostAsJsonAsync(Url(other.Id), new { description = "Other project", category = "Travel", amountUsd = 10m, expenseDate = Yesterday });

        var page1 = await (await _client.GetAsync($"{Url(project.Id)}?status=Pending&page=1&pageSize=2")).ReadAsync<PagedResponse<ExpenseResponse>>();
        var approved = await (await _client.GetAsync($"{Url(project.Id)}?status=Approved")).ReadAsync<PagedResponse<ExpenseResponse>>();

        Assert.Equal(3, page1.TotalCount);
        Assert.Equal(["Item 1", "Item 3"], page1.Items.Select(e => e.Description));
        Assert.Equal(0, approved.TotalCount);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync(Url(Guid.NewGuid()))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.GetAsync($"{Url(project.Id)}?status=Paid")).StatusCode);
    }

    private static string Url(Guid projectId) => $"/api/v1/projects/{projectId}/expenses";
}
