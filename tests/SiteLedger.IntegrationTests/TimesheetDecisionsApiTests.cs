using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SiteLedger.Application.Contracts.Timesheets;
using SiteLedger.Application.Exceptions;
using SiteLedger.Domain.Enums;
using SiteLedger.Infrastructure.Persistence;
using SiteLedger.IntegrationTests.Fixtures;

namespace SiteLedger.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class TimesheetDecisionsApiTests(SiteLedgerApiFactory factory) : IAsyncLifetime
{
    private static readonly DateOnly Yesterday = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);
    private readonly HttpClient _client = factory.CreateTrustedClient();

    public Task InitializeAsync() => factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact] // I07
    public async Task Approve_ChangesStatus_AndWritesExactlyOneMatchingAudit()
    {
        var timesheet = await PendingTimesheetAsync();

        var response = await _client.PostAsJsonAsync(Approve(timesheet.Id), new { note = "Verified against supervisor log" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.ReadAsync<TimesheetResponse>();
        Assert.Equal(SubmissionStatus.Approved, body.Status);
        Assert.NotNull(body.DecidedBySubject);
        Assert.NotNull(body.DecidedAtUtc);

        var (row, audits) = await LoadAsync(timesheet.Id);
        Assert.Equal(SubmissionStatus.Approved, row.Status);
        var audit = Assert.Single(audits);
        Assert.Equal(SubmissionType.Timesheet, audit.SubmissionType);
        Assert.Equal(AuditAction.Approved, audit.Action);
        Assert.Equal("Verified against supervisor log", audit.Reason);
        Assert.Equal(row.DecidedBySubject, audit.ActorSubject);
        Assert.Equal(row.DecidedAtUtc, audit.OccurredAtUtc);
    }

    [Fact] // I08
    public async Task Reject_ThenLaterDecisions_Return409_AndTheRejectionStands()
    {
        var timesheet = await PendingTimesheetAsync();

        var reject = await _client.PostAsJsonAsync(RejectUrl(timesheet.Id), new { reason = "Hours do not match the work order" });
        var approveLater = await _client.PostAsJsonAsync(Approve(timesheet.Id), new { });
        var rejectAgain = await _client.PostAsJsonAsync(RejectUrl(timesheet.Id), new { reason = "Second opinion" });

        Assert.Equal(HttpStatusCode.OK, reject.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, approveLater.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, rejectAgain.StatusCode);

        var (row, audits) = await LoadAsync(timesheet.Id);
        Assert.Equal(SubmissionStatus.Rejected, row.Status);
        var audit = Assert.Single(audits);
        Assert.Equal(AuditAction.Rejected, audit.Action);
        Assert.Equal("Hours do not match the work order", audit.Reason);
    }

    [Fact]
    public async Task InvalidDecisions_Return400Or404_AndWriteNothing_WhileEmptyApproveBodyIsAllowed()
    {
        var timesheet = await PendingTimesheetAsync();

        var shortReason = await _client.PostAsJsonAsync(RejectUrl(timesheet.Id), new { reason = "no" });
        var noReason = await _client.PostAsJsonAsync(RejectUrl(timesheet.Id), new { });
        var unknown = await _client.PostAsJsonAsync(Approve(Guid.NewGuid()), new { });

        Assert.Equal(HttpStatusCode.BadRequest, shortReason.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, noReason.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        var (pending, noAudits) = await LoadAsync(timesheet.Id);
        Assert.Equal(SubmissionStatus.Pending, pending.Status);
        Assert.Empty(noAudits);

        var emptyBody = await _client.PostAsync(Approve(timesheet.Id), content: null);
        Assert.Equal(HttpStatusCode.OK, emptyBody.StatusCode);
    }

    // Isolates rowversion: the second save carries a stale RowVersion, matches 0 rows and fails.
    [Fact]
    public async Task StaleUpdate_IsRejectedByRowVersion()
    {
        var timesheet = await PendingTimesheetAsync();
        await using var scopeA = factory.Services.CreateAsyncScope();
        await using var scopeB = factory.Services.CreateAsyncScope();
        var dbA = scopeA.ServiceProvider.GetRequiredService<SiteLedgerDbContext>();
        var dbB = scopeB.ServiceProvider.GetRequiredService<SiteLedgerDbContext>();

        var copyA = await dbA.Timesheets.SingleAsync(t => t.Id == timesheet.Id);
        var copyB = await dbB.Timesheets.SingleAsync(t => t.Id == timesheet.Id); // both see Pending

        copyA.Approve("approver-a", null, DateTimeOffset.UtcNow);
        await dbA.SaveChangesAsync();

        copyB.Approve("approver-b", null, DateTimeOffset.UtcNow); // passes the in-memory Pending check
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => dbB.SaveChangesAsync());
    }

    // Real decision path with a stale read: the loser's status change and audit fail as one unit.
    [Fact]
    public async Task StaleDecision_FailsAtomically_LeavingOnlyTheWinnersStatusAndAudit()
    {
        var timesheet = await PendingTimesheetAsync();
        await using var scopeA = factory.Services.CreateAsyncScope();
        await using var scopeB = factory.Services.CreateAsyncScope();
        var dbA = scopeA.ServiceProvider.GetRequiredService<SiteLedgerDbContext>();
        var dbB = scopeB.ServiceProvider.GetRequiredService<SiteLedgerDbContext>();
        var copyA = await dbA.Timesheets.SingleAsync(t => t.Id == timesheet.Id);
        var copyB = await dbB.Timesheets.SingleAsync(t => t.Id == timesheet.Id);

        dbA.ApprovalAudits.Add(copyA.Approve("approver-a", null, DateTimeOffset.UtcNow));
        await dbA.SaveChangesAsync();

        dbB.ApprovalAudits.Add(copyB.Reject("approver-b", "Conflicting decision", DateTimeOffset.UtcNow));
        var ex = await Record.ExceptionAsync(() => dbB.SaveChangesAsync());

        // Whichever statement SQL Server rejects first, the whole transaction rolls back.
        Assert.True(ex is DbUpdateConcurrencyException or DuplicateResourceException, $"Unexpected: {ex?.GetType().Name}");
        var (row, audits) = await LoadAsync(timesheet.Id);
        Assert.Equal(SubmissionStatus.Approved, row.Status);
        Assert.Equal("approver-a", row.DecidedBySubject);
        var audit = Assert.Single(audits);
        Assert.Equal("approver-a", audit.ActorSubject);
        Assert.Equal(AuditAction.Approved, audit.Action);
    }

    [Fact] // I15
    public async Task ParallelApprovals_ExactlyOneSucceeds_OthersGet409_AndOneAuditExists()
    {
        const int approvers = 5;
        var timesheet = await PendingTimesheetAsync();

        var responses = await Task.WhenAll(Enumerable.Range(0, approvers)
            .Select(_ => factory.CreateTrustedClient().PostAsJsonAsync(Approve(timesheet.Id), new { note = "parallel" })));

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(approvers - 1, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        var (row, audits) = await LoadAsync(timesheet.Id);
        Assert.Equal(SubmissionStatus.Approved, row.Status);
        Assert.Single(audits);
    }

    // Defense in depth: data changed outside the API (e.g. by a DBA) must still block decisions.
    [Fact]
    public async Task Decision_OnProjectCompletedOutsideTheApi_Is409_AndWritesNoAudit()
    {
        var timesheet = await PendingTimesheetAsync();
        await factory.QueryDbAsync(db => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE Projects SET Status = 'Completed' WHERE Id = {timesheet.ProjectId}"));

        var response = await _client.PostAsync(Approve(timesheet.Id), content: null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var (row, audits) = await LoadAsync(timesheet.Id);
        Assert.Equal(SubmissionStatus.Pending, row.Status);
        Assert.Empty(audits);
    }

    private async Task<TimesheetResponse> PendingTimesheetAsync()
    {
        var project = await _client.CreateProjectAsync("CON-2026-001");
        var employee = await factory.AddEmployeeAsync(hourlyRateUsd: 55m);
        return await _client.SubmitTimesheetAsync(project.Id, employee.Id, Yesterday);
    }

    private Task<(Domain.Entities.Timesheet Row, List<Domain.Entities.ApprovalAudit> Audits)> LoadAsync(Guid timesheetId) =>
        factory.QueryDbAsync(async db => (
            await db.Timesheets.AsNoTracking().SingleAsync(t => t.Id == timesheetId),
            await db.ApprovalAudits.AsNoTracking().Where(a => a.SubmissionId == timesheetId).ToListAsync()));

    private static string Approve(Guid id) => $"/api/v1/timesheets/{id}/approve";

    private static string RejectUrl(Guid id) => $"/api/v1/timesheets/{id}/reject";
}
