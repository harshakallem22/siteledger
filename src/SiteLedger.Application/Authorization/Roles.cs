namespace SiteLedger.Application.Authorization;

public static class Roles
{
    public const string ProjectManager = "ProjectManager";
    public const string Approver = "Approver";
    public const string Viewer = "Viewer";

    public static readonly IReadOnlyList<string> All = [ProjectManager, Approver, Viewer];
}
