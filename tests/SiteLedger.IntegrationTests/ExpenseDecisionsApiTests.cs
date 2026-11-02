using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SiteLedger.Application.Contracts.Expenses;
using SiteLedger.Application.Exceptions;
using SiteLedger.Domain.Entities;
using SiteLedger.Domain.Enums;
using SiteLedger.Infrastructure.Persistence;
using SiteLedger.IntegrationTests.Fixtures;

namespace SiteLedger.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class ExpenseDecisionsApiTests(SiteLedgerApiFactory factory) : IAsyncLifetime
{
    private readonly HttpClient _client = factory.CreateTrustedClient();

    public Task InitializeAsync() => factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact] // I10
    public async Task Approve_ChangesStatus_AndWritesExactlyOneExpenseAudit()
    {
        var expense = await PendingExpenseAsync();

        var response = await _client.PostAsJsonAsync(ApproveUrl(expense.Id), new { note = "Invoice matched purchase order" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(SubmissionStatus.Approved, (await response.ReadAsync<ExpenseResponse>()).Status);

        var (row, audits) = await LoadAsync(expense.Id);
        Assert.Equal(SubmissionStatus.Approved, row.Status);
        var audit = Assert.Single(audits);
        Assert.Equal(SubmissionType.Expense, audit.SubmissionType);
        Assert.Equal(AuditAction.Approved, audit.Action);
        Assert.Equal("Invoice matched purchase order", audit.Reason);
        Assert.Equal(row.DecidedBySubject, audit.ActorSubject);
        Assert.Equal(row.DecidedAtUtc, audit.OccurredAtUtc);
    }

    [Fact] // I11
    public async Task Reject_StoresReasonInAudit_AndLaterApprovalIs409()
    {
        var expense = await PendingExpenseAsync();

        var reject = await _client.PostAsJsonAsync(RejectUrl(expense.Id), new { reason = "Supporting invoice is missing" });
        var approveLater = await _client.PostAsync(ApproveUrl(expense.Id), content: null);

        Assert.Equal(HttpStatusCode.OK, reject.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, approveLater.StatusCode);
        var (row, audits) = await LoadAsync(expense.Id);
        Assert.Equal(SubmissionStatus.Rejected, row.Status);
        var audit = Assert.Single(audits);
        Assert.Equal(AuditAction.Rejected, audit.Action);
        Assert.Equal("Supporting invoice is missing", audit.Reason);
    }

    [Fact]
    public async Task InvalidDecisions_Return400Or404_AndWriteNothing()
    {
        var expense = await PendingExpenseAsync();

        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsJsonAsync(RejectUrl(expense.Id), new { reason = "no" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.PostAsync(ApproveUrl(Guid.NewGuid()), content: null)).StatusCode);

        // Expense routes must not decide timesheets.
        var project = await _client.CreateProjectAsync("CON-OTHER");
        var employee = await factory.AddEmployeeAsync(hourlyRateUsd: 50m);
        var timesheet = await _client.SubmitTimesheetAsync(project.Id, employee.Id, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1));
        Assert.Equal(HttpStatusCode.NotFound, (await _client.PostAsync(ApproveUrl(timesheet.Id), content: null)).StatusCode);

        var (row, audits) = await LoadAsync(expense.Id);
        Assert.Equal(SubmissionStatus.Pending, row.Status);
        Assert.Empty(audits);
        Assert.Equal(0, await factory.QueryDbAsync(db => db.ApprovalAudits.CountAsync()));
    }

    [Fact]
    public async Task StaleDecision_FailsAtomically_LeavingOnlyTheWinnersStatusAndAudit()
    {
        var expense = await PendingExpenseAsync();
        await using var scopeA = factory.Services.CreateAsyncScope();
        await using var scopeB = factory.Services.CreateAsyncScope();
        var dbA = scopeA.ServiceProvider.GetRequiredService<SiteLedgerDbContext>();
        var dbB = scopeB.ServiceProvider.GetRequiredService<SiteLedgerDbContext>();
        var copyA = await dbA.Expenses.SingleAsync(e => e.Id == expense.Id);
        var copyB = await dbB.Expenses.SingleAsync(e => e.Id == expense.Id);

        dbA.ApprovalAudits.Add(copyA.Approve("approver-a", null, DateTimeOffset.UtcNow));
        await dbA.SaveChangesAsync();

        dbB.ApprovalAudits.Add(copyB.Reject("approver-b", "Conflicting decision", DateTimeOffset.UtcNow));
        var ex = await Record.ExceptionAsync(() => dbB.SaveChangesAsync());

        Assert.True(ex is DbUpdateConcurrencyException or DuplicateResourceException, $"Unexpected: {ex?.GetType().Name}");
        var (row, audits) = await LoadAsync(expense.Id);
        Assert.Equal(SubmissionStatus.Approved, row.Status);
        Assert.Equal("approver-a", row.DecidedBySubject);
        Assert.Equal("approver-a", Assert.Single(audits).ActorSubject);
    }

    [Fact]
    public async Task ParallelDecisions_ApproveAndRejectRacing_ExactlyOneWins_AndOneAuditExists()
    {
        var expense = await PendingExpenseAsync();

        // Mixed race: 3 approvers and 3 rejecters at once.
        var requests = Enumerable.Range(0, 6).Select(i => i % 2 == 0
            ? factory.CreateTrustedClient().PostAsJsonAsync(ApproveUrl(expense.Id), new { note = $"approver {i}" })
            : factory.CreateTrustedClient().PostAsJsonAsync(RejectUrl(expense.Id), new { reason = $"rejecter {i} says no" }));
        var responses = await Task.WhenAll(requests);

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(5, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));

        var (row, audits) = await LoadAsync(expense.Id);
        var audit = Assert.Single(audits);
        var expectedAction = row.Status == SubmissionStatus.Approved ? AuditAction.Approved : AuditAction.Rejected;
        Assert.Equal(expectedAction, audit.Action); // the stored status and the audit always agree
    }

    private async Task<ExpenseResponse> PendingExpenseAsync()
    {
        var project = await _client.CreateProjectAsync("CON-2026-001");
        return await _client.SubmitExpenseAsync(project.Id);
    }

    private Task<(Expense Row, List<ApprovalAudit> Audits)> LoadAsync(Guid expenseId) =>
        factory.QueryDbAsync(async db => (
            await db.Expenses.AsNoTracking().SingleAsync(e => e.Id == expenseId),
            await db.ApprovalAudits.AsNoTracking().Where(a => a.SubmissionId == expenseId).ToListAsync()));

    private static string ApproveUrl(Guid id) => $"/api/v1/expenses/{id}/approve";

    private static string RejectUrl(Guid id) => $"/api/v1/expenses/{id}/reject";
}
