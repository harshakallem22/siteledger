using SiteLedger.Domain.Enums;
using SiteLedger.Domain.Exceptions;

namespace SiteLedger.Domain.Rules;

public static class ApprovalRules
{
    public const int ReasonMinLength = 5;
    public const int ReasonMaxLength = 500;

    // Approved and Rejected are final.
    public static void EnsurePending(SubmissionStatus current)
    {
        if (current != SubmissionStatus.Pending)
        {
            throw new DomainConflictException($"Only Pending submissions can be approved or rejected; this one is already {current}.");
        }
    }

    public static string? NormalizeNote(string? note)
    {
        var trimmed = note?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        return trimmed.Length <= ReasonMaxLength
            ? trimmed
            : throw new DomainValidationException("note", $"Note can be at most {ReasonMaxLength} characters.");
    }

    public static string NormalizeRejectionReason(string? reason)
    {
        var trimmed = reason?.Trim() ?? string.Empty;
        return trimmed.Length is >= ReasonMinLength and <= ReasonMaxLength
            ? trimmed
            : throw new DomainValidationException("reason", $"Reason must be {ReasonMinLength}-{ReasonMaxLength} characters.");
    }
}
