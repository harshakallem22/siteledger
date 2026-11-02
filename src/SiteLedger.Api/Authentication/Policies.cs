using Microsoft.AspNetCore.Authorization;
using SiteLedger.Application.Authorization;

namespace SiteLedger.Api.Authentication;

// Global roles (no per-project access in the MVP): ProjectManager writes, Approver decides, all three read.
public static class Policies
{
    public const string Read = "Read";
    public const string ManageProjects = "ManageProjects";
    public const string Decide = "Decide";

    public static void Configure(AuthorizationOptions options)
    {
        options.AddPolicy(Read, policy => policy.RequireClaim("sub").RequireRole(Roles.ProjectManager, Roles.Approver, Roles.Viewer));
        options.AddPolicy(ManageProjects, policy => policy.RequireClaim("sub").RequireRole(Roles.ProjectManager));
        options.AddPolicy(Decide, policy => policy.RequireClaim("sub").RequireRole(Roles.Approver));

        // Any endpoint someone forgets to annotate still requires a signed-in user.
        options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
    }
}
