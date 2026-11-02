namespace SiteLedger.Application.Exceptions;

// → HTTP 404.
public sealed class ResourceNotFoundException(string resource, Guid id)
    : Exception($"{resource} '{id}' was not found.");
