using SiteLedger.Application.Abstractions;

namespace SiteLedger.Api.Authentication;

internal sealed class HttpCurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    // Every policy requires "sub", so a missing claim is a bug, never a reason to fall back to a default actor.
    public string Subject =>
        httpContextAccessor.HttpContext?.User.FindFirst("sub")?.Value
        ?? throw new InvalidOperationException("No authenticated subject is available for this request.");
}
