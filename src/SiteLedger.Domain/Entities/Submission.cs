using SiteLedger.Domain.Enums;
using SiteLedger.Domain.Rules;

namespace SiteLedger.Domain.Entities;

// Shared by Timesheet and Expense; plain C# base class, so each keeps its own table (no EF inheritance mapping).
public abstract class Submission
{
    // For EF Core.
    protected Submission()
    {
    }

    protected Submission(Guid projectId, string submittedBySubject, DateTimeOffset submittedAtUtc)
    {
        Id = Guid.NewGuid();
        ProjectId = projectId;
        Status = SubmissionStatus.Pending;
        SubmittedBySubject = submittedBySubject;
        SubmittedAtUtc = submittedAtUtc;
    }

    public Guid Id { get; private set; }

    public Guid ProjectId { get; private set; }

    public SubmissionStatus Status { get; private set; }

    public string SubmittedBySubject { get; private set; } = string.Empty;

    // Null while Pending.
    public string? DecidedBySubject { get; private set; }

    public DateTimeOffset SubmittedAtUtc { get; private set; }

    public DateTimeOffset? DecidedAtUtc { get; private set; }

    // A stale update matches 0 rows and fails (optimistic concurrency).
    public byte[] RowVersion { get; private set; } = [];

    protected abstract SubmissionType AuditType { get; }

    // Returns the audit row; save both in ONE SaveChanges so status and audit commit together.
    public ApprovalAudit Approve(string actorSubject, string? note, DateTimeOffset nowUtc)
    {
        var normalizedNote = ApprovalRules.NormalizeNote(note);
        return Decide(SubmissionStatus.Approved, AuditAction.Approved, actorSubject, normalizedNote, nowUtc);
    }

    public ApprovalAudit Reject(string actorSubject, string? reason, DateTimeOffset nowUtc)
    {
        var normalizedReason = ApprovalRules.NormalizeRejectionReason(reason);
        return Decide(SubmissionStatus.Rejected, AuditAction.Rejected, actorSubject, normalizedReason, nowUtc);
    }

    private ApprovalAudit Decide(SubmissionStatus outcome, AuditAction action, string actorSubject, string? reason, DateTimeOffset nowUtc)
    {
        ApprovalRules.EnsurePending(Status);

        Status = outcome;
        DecidedBySubject = actorSubject;
        DecidedAtUtc = nowUtc;

        return new ApprovalAudit(AuditType, Id, action, actorSubject, nowUtc, reason);
    }
}
