using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Testcontainers.MsSql;

namespace SiteLedger.IntegrationTests.Fixtures;

// Configured DB (SITELEDGER_TEST_SQL or user-secret) else a Testcontainers SQL Server; SITELEDGER_TEST_DB=external|container forces one.
internal static class TestDatabase
{
    public const string ConnectionVariable = "SITELEDGER_TEST_SQL";
    public const string ModeVariable = "SITELEDGER_TEST_DB";
    public const string ContainerImage = "mcr.microsoft.com/mssql/server:2022-latest";
    private const string ContainerDatabaseName = "siteledger_test";

    public sealed record Resolved(string ConnectionString, string Source, MsSqlContainer? Container);

    public static async Task<Resolved> ResolveAsync()
    {
        var mode = Environment.GetEnvironmentVariable(ModeVariable)?.Trim().ToLowerInvariant();
        var configured = ConfiguredConnectionString();

        if (mode == "container" || (mode is null or "" && configured is null))
        {
            return await StartContainerAsync();
        }

        if (configured is null)
        {
            throw new InvalidOperationException(
                $"{ModeVariable}=external but no database is configured. Set {ConnectionVariable} or the user-secret " +
                "ConnectionStrings:Test for tests/SiteLedger.IntegrationTests.");
        }

        EnsureIsTestDatabase(new SqlConnectionStringBuilder(configured).InitialCatalog);
        return new Resolved(configured, "configured database", Container: null);
    }

    public static void EnsureIsTestDatabase(string databaseName)
    {
        if (!databaseName.Contains("test", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Refusing to use database '{databaseName}': integration tests delete data, so the name must contain 'test'.");
        }
    }

    private static string? ConfiguredConnectionString()
    {
        var fromEnvironment = Environment.GetEnvironmentVariable(ConnectionVariable);
        if (!string.IsNullOrWhiteSpace(fromEnvironment))
        {
            return fromEnvironment;
        }

        var configuration = new ConfigurationBuilder()
            .AddUserSecrets(typeof(TestDatabase).Assembly, optional: true)
            .Build();
        var fromSecrets = configuration.GetConnectionString("Test");
        return string.IsNullOrWhiteSpace(fromSecrets) ? null : fromSecrets;
    }

    private static async Task<Resolved> StartContainerAsync()
    {
        var container = new MsSqlBuilder(ContainerImage).Build();
        await container.StartAsync();

        // The container starts on "master"; tests get their own database, created by EF migrations.
        var connection = new SqlConnectionStringBuilder(container.GetConnectionString())
        {
            InitialCatalog = ContainerDatabaseName,
        };
        return new Resolved(connection.ConnectionString, $"Testcontainers ({ContainerImage})", container);
    }
}
