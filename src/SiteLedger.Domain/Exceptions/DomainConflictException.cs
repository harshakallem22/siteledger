namespace SiteLedger.Domain.Exceptions;

// Valid request, but not allowed in the current state → HTTP 409.
public sealed class DomainConflictException(string message, Exception? innerException = null)
    : Exception(message, innerException);
