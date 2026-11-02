namespace SiteLedger.Domain.Enums;

// Only Pending can move to Approved or Rejected.
public enum SubmissionStatus
{
    Pending,
    Approved,
    Rejected,
}
