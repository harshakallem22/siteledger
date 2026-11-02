using SiteLedger.Domain.Entities;
using SiteLedger.Domain.Enums;
using SiteLedger.Domain.Exceptions;

namespace SiteLedger.UnitTests.Domain;

public sealed class ApprovalRulesTests
{
    private static readonly DateTimeOffset SubmittedAt = new(2026, 10, 28, 16, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset DecidedAt = SubmittedAt.AddHours(3);

    private static Timesheet PendingTimesheet()
    {
        var project = Project.Create("CON-2026-001", "Fremont Equipment Bay Upgrade", 250_000m, ProjectStatus.Active, SubmittedAt);
        var employee = new Employee(Guid.NewGuid(), "Priya Patel", "priya@siteledger.example", 55m, isActive: true, SubmittedAt);
        return Timesheet.Submit(project, employee, new DateOnly(2026, 10, 27), 8m, "pm-1", SubmittedAt);
    }

    [Fact] // U06
    public void Approve_Pending_BecomesApproved_AndProducesMatchingAudit()
    {
        var timesheet = PendingTimesheet();

        var audit = timesheet.Approve("approver-1", "  Verified against supervisor log  ", DecidedAt);

        Assert.Equal(SubmissionStatus.Approved, timesheet.Status);
        Assert.Equal("approver-1", timesheet.DecidedBySubject);
        Assert.Equal(DecidedAt, timesheet.DecidedAtUtc);

        Assert.Equal(SubmissionType.Timesheet, audit.SubmissionType);
        Assert.Equal(timesheet.Id, audit.SubmissionId);
        Assert.Equal(AuditAction.Approved, audit.Action);
        Assert.Equal("approver-1", audit.ActorSubject);
        Assert.Equal(DecidedAt, audit.OccurredAtUtc);
        Assert.Equal("Verified against supervisor log", audit.Reason);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public void Approve_WithoutNote_StoresNullReason(string? note)
    {
        var audit = PendingTimesheet().Approve("approver-1", note, DecidedAt);

        Assert.Null(audit.Reason);
    }

    [Fact] // U07
    public void Reject_Pending_BecomesRejected_WithReasonInAudit()
    {
        var timesheet = PendingTimesheet();

        var audit = timesheet.Reject("approver-1", "Hours do not match the work order", DecidedAt);

        Assert.Equal(SubmissionStatus.Rejected, timesheet.Status);
        Assert.Equal(AuditAction.Rejected, audit.Action);
        Assert.Equal("Hours do not match the work order", audit.Reason);
    }

    [Theory] // U08
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  no ")]  // 2 characters after trimming
    [InlineData("abcd")]   // 4 characters
    public void Reject_WithMissingOrShortReason_IsAValidationError_AndChangesNothing(string? reason)
    {
        var timesheet = PendingTimesheet();

        var ex = Assert.Throws<DomainValidationException>(() => timesheet.Reject("approver-1", reason, DecidedAt));

        Assert.Equal("reason", ex.Field);
        Assert.Equal(SubmissionStatus.Pending, timesheet.Status);
        Assert.Null(timesheet.DecidedBySubject);
    }

    [Fact]
    public void Reject_WithReasonOver500Characters_IsAValidationError()
    {
        var ex = Assert.Throws<DomainValidationException>(() =>
            PendingTimesheet().Reject("approver-1", new string('x', 501), DecidedAt));

        Assert.Equal("reason", ex.Field);
    }

    [Fact]
    public void Approve_WithNoteOver500Characters_IsAValidationError()
    {
        var ex = Assert.Throws<DomainValidationException>(() =>
            PendingTimesheet().Approve("approver-1", new string('x', 501), DecidedAt));

        Assert.Equal("note", ex.Field);
    }

    [Fact] // U09
    public void Approved_IsFinal_CannotBeApprovedOrRejectedAgain()
    {
        var timesheet = PendingTimesheet();
        timesheet.Approve("approver-1", null, DecidedAt);

        Assert.Throws<DomainConflictException>(() => timesheet.Approve("approver-2", null, DecidedAt.AddMinutes(1)));
        Assert.Throws<DomainConflictException>(() => timesheet.Reject("approver-2", "Changed my mind", DecidedAt.AddMinutes(1)));
        Assert.Equal("approver-1", timesheet.DecidedBySubject);
        Assert.Equal(DecidedAt, timesheet.DecidedAtUtc);
    }

    [Fact] // U10
    public void Rejected_IsFinal_CannotBeApprovedLater()
    {
        var timesheet = PendingTimesheet();
        timesheet.Reject("approver-1", "Missing supervisor sign-off", DecidedAt);

        Assert.Throws<DomainConflictException>(() => timesheet.Approve("approver-2", null, DecidedAt.AddMinutes(1)));
        Assert.Equal(SubmissionStatus.Rejected, timesheet.Status);
    }
}
