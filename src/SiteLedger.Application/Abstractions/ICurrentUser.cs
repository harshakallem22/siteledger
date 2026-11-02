namespace SiteLedger.Application.Abstractions;

// From the validated JWT "sub" claim, never from the request body.
public interface ICurrentUser
{
    string Subject { get; }
}
