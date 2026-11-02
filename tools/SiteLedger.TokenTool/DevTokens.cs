using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace SiteLedger.TokenTool;

// Dev/test tokens only, not an identity provider: anyone with the signing key can mint them.
public static class DevTokens
{
    public static string Create(
        string issuer,
        string audience,
        string signingKey,
        string subject,
        IReadOnlyCollection<string> roles,
        TimeSpan lifetime,
        DateTimeOffset nowUtc)
    {
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            IssuedAt = nowUtc.UtcDateTime,
            NotBefore = nowUtc.UtcDateTime,
            Expires = nowUtc.Add(lifetime).UtcDateTime,
            Claims = new Dictionary<string, object>
            {
                ["sub"] = subject,
                ["jti"] = Guid.NewGuid().ToString("N"),
                ["role"] = roles.ToArray(), // serialized as a JSON array → one role claim per entry
            },
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
                SecurityAlgorithms.HmacSha256),
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }
}
