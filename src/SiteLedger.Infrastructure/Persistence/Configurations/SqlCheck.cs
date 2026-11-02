namespace SiteLedger.Infrastructure.Persistence.Configurations;

internal static class SqlCheck
{
    // Built from the enum, so the constraint can never drift from the code.
    public static string InEnum<TEnum>(string column)
        where TEnum : struct, Enum =>
        $"[{column}] IN ({string.Join(", ", Enum.GetNames<TEnum>().Select(name => $"N'{name}'"))})";

    // Pending rows have no decision data; decided rows must have both decider and time.
    public const string DecisionMatchesStatus =
        "([Status] = N'Pending' AND [DecidedBySubject] IS NULL AND [DecidedAtUtc] IS NULL) OR " +
        "([Status] <> N'Pending' AND [DecidedBySubject] IS NOT NULL AND [DecidedAtUtc] IS NOT NULL)";
}
