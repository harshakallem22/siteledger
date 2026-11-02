using Microsoft.Extensions.Configuration;
using SiteLedger.Application.Authorization;
using SiteLedger.TokenTool;

// Usage: dotnet run --project tools/SiteLedger.TokenTool -- --role Approver [--sub id] [--minutes 60] (prints only the token).

// Same sources as the API locally: its user-secrets, then environment variables.
const string ApiUserSecretsId = "62e2a992-ae11-4a33-8674-632f26666d8b";
var configuration = new ConfigurationBuilder()
    .AddUserSecrets(ApiUserSecretsId)
    .AddEnvironmentVariables()
    .Build();

var options = ParseArguments(args);
if (options is null)
{
    Console.Error.WriteLine("Usage: --role <ProjectManager|Approver|Viewer> [--role ...] [--sub <subject>] [--minutes <1-480>]");
    return 1;
}

var signingKey = configuration["Jwt:SigningKey"];
if (string.IsNullOrWhiteSpace(signingKey))
{
    Console.Error.WriteLine("Jwt:SigningKey not found in the API's user-secrets or the Jwt__SigningKey environment variable.");
    return 1;
}

var token = DevTokens.Create(
    configuration["Jwt:Issuer"] ?? "SiteLedger",
    configuration["Jwt:Audience"] ?? "SiteLedger.Api",
    signingKey,
    options.Value.Subject,
    options.Value.Roles,
    TimeSpan.FromMinutes(options.Value.Minutes),
    DateTimeOffset.UtcNow);

Console.WriteLine(token);
Console.Error.WriteLine($"Token for sub={options.Value.Subject}, roles={string.Join(",", options.Value.Roles)}, valid {options.Value.Minutes} min.");
return 0;

static (string Subject, List<string> Roles, int Minutes)? ParseArguments(string[] args)
{
    var roles = new List<string>();
    string? subject = null;
    var minutes = 60;

    for (var i = 0; i + 1 < args.Length; i += 2)
    {
        switch (args[i])
        {
            case "--role":
                var role = Roles.All.FirstOrDefault(r => r.Equals(args[i + 1], StringComparison.OrdinalIgnoreCase));
                if (role is null)
                {
                    return null;
                }

                roles.Add(role);
                break;
            case "--sub":
                subject = args[i + 1];
                break;
            case "--minutes" when int.TryParse(args[i + 1], out var parsed) && parsed is >= 1 and <= 480:
                minutes = parsed;
                break;
            default:
                return null;
        }
    }

    return roles.Count == 0 ? null : (subject ?? $"{roles[0].ToLowerInvariant()}-dev", roles, minutes);
}
