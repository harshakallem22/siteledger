using SiteLedger.Domain.Enums;

namespace SiteLedger.Domain.Entities;

// Immutable decision record; (SubmissionType, SubmissionId) can point at either table, so no FK is possible.
public sealed class ApprovalAudit
{
    private ApprovalAudit()
    {
    }

    public ApprovalAudit(
        SubmissionType submissionType,
        Guid submissionId,
        AuditAction action,
        string actorSubject,
        DateTimeOffset occurredAtUtc,
        string? reason)
    {
        Id = Guid.NewGuid();
        SubmissionType = submissionType;
        SubmissionId = submissionId;
        Action = action;
        ActorSubject = actorSubject;
        OccurredAtUtc = occurredAtUtc;
        Reason = reason;
    }

    public Guid Id { get; private set; }

    public SubmissionType SubmissionType { get; private set; }

    public Guid SubmissionId { get; private set; }

    public AuditAction Action { get; private set; }

    public string ActorSubject { get; private set; } = string.Empty;

    public DateTimeOffset OccurredAtUtc { get; private set; }

    // Rejection reason or optional approval note.
    public string? Reason { get; private set; }
}
