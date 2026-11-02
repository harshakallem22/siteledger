namespace SiteLedger.Domain.Exceptions;

// Input breaks a business rule → HTTP 400.
public sealed class DomainValidationException(string field, string message) : Exception(message)
{
    // JSON name of the offending field, e.g. "budgetUsd".
    public string Field { get; } = field;
}
