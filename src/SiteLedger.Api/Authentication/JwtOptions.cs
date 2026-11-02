using System.ComponentModel.DataAnnotations;

namespace SiteLedger.Api.Authentication;

// Validated at startup; SigningKey comes from user-secrets or environment variables, never Git.
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    [Required]
    public string Issuer { get; init; } = string.Empty;

    [Required]
    public string Audience { get; init; } = string.Empty;

    // HS256 needs at least 256 bits (32 characters).
    [Required]
    [MinLength(32)]
    public string SigningKey { get; init; } = string.Empty;
}
