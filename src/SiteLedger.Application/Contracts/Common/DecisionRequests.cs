namespace SiteLedger.Application.Contracts.Common;

// Optional body: empty means "approve with no note".
public sealed record ApproveRequest
{
    public string? Note { get; init; }
}

// Reason is required (5–500 characters, enforced by the domain).
public sealed record RejectRequest
{
    public string? Reason { get; init; }
}
