using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SiteLedger.Application.Authorization;
using SiteLedger.Infrastructure.Persistence;
using SiteLedger.TokenTool;

namespace SiteLedger.IntegrationTests.Fixtures;

// Hosts the real API in memory against a real SQL Server; shared by every test class in the "Api" collection.
public sealed class SiteLedgerApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string Issuer = "SiteLedger";
    public const string Audience = "SiteLedger.Api";

    private TestDatabase.Resolved? _database;

    // Fresh random key per run, unrelated to any developer's key.
    public string SigningKey { get; } = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));

    public static JsonSerializerOptions Json { get; } = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // "Testing", not Development, so the API's own user-secrets (the dev database) are never loaded.
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", Database.ConnectionString);
        builder.UseSetting("Jwt:SigningKey", SigningKey);
    }

    public string CreateToken(string subject, params string[] roles) =>
        DevTokens.Create(Issuer, Audience, SigningKey, subject, roles, TimeSpan.FromMinutes(30), DateTimeOffset.UtcNow);

    public HttpClient CreateClientAs(string subject, params string[] roles)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken(subject, roles));
        return client;
    }

    // All three roles: for behavior tests; permissions are tested role by role in AuthorizationTests.
    public HttpClient CreateTrustedClient() => CreateClientAs("test-user", [.. Roles.All]);

    public string DatabaseSource => Database.Source;

    private TestDatabase.Resolved Database =>
        _database ?? throw new InvalidOperationException("The test database is resolved in InitializeAsync, before the host starts.");

    public async Task InitializeAsync()
    {
        // Must run before anything touches Services: building the host reads the connection string.
        _database = await TestDatabase.ResolveAsync();

        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SiteLedgerDbContext>();
        TestDatabase.EnsureIsTestDatabase(db.Database.GetDbConnection().Database);
        await db.Database.MigrateAsync();
    }

    // Children before parents, so each test starts from an empty database.
    public Task ResetDatabaseAsync() =>
        QueryDbAsync(db =>
        {
            TestDatabase.EnsureIsTestDatabase(db.Database.GetDbConnection().Database);
            return db.Database.ExecuteSqlRawAsync(
                "DELETE FROM ApprovalAudits; DELETE FROM Timesheets; DELETE FROM Expenses; DELETE FROM Projects; DELETE FROM Employees;");
        });

    // Runs inside EF's retry strategy (e.g. Azure 40613), with a fresh DbContext per attempt.
    public async Task<T> QueryDbAsync<T>(Func<SiteLedgerDbContext, Task<T>> query)
    {
        await using var scope = Services.CreateAsyncScope();
        var strategy = scope.ServiceProvider.GetRequiredService<SiteLedgerDbContext>().Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            await using var attempt = Services.CreateAsyncScope();
            return await query(attempt.ServiceProvider.GetRequiredService<SiteLedgerDbContext>());
        });
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await DisposeAsync();
        if (_database?.Container is { } container)
        {
            await container.DisposeAsync(); // stops and removes the throwaway SQL Server
        }
    }
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<SiteLedgerApiFactory>
{
    public const string Name = "Api";
}
