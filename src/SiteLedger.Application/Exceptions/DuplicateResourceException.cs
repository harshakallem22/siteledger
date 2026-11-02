namespace SiteLedger.Application.Exceptions;

// A unique constraint rejected the write → HTTP 409.
public sealed class DuplicateResourceException(string message, Exception? innerException = null)
    : Exception(message, innerException);
