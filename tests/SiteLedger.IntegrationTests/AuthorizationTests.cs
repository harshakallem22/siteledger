using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using SiteLedger.Application.Authorization;
using SiteLedger.Application.Contracts.Expenses;
using SiteLedger.Application.Contracts.Timesheets;
using SiteLedger.IntegrationTests.Fixtures;
using SiteLedger.TokenTool;

namespace SiteLedger.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed partial class AuthorizationTests(SiteLedgerApiFactory factory) : IAsyncLifetime
{
    private const string Pm = Roles.ProjectManager;
    private const string Approver = Roles.Approver;
    private const string Viewer = Roles.Viewer;

    // Access contract for all 14 business operations (spec §7.1); {id} = any GUID.
    public static TheoryData<string, string, string[]> Endpoints => new()
    {
        { "POST", "/api/v1/projects", [Pm] },
        { "GET", "/api/v1/projects", [Pm, Approver, Viewer] },
        { "GET", "/api/v1/projects/{id}", [Pm, Approver, Viewer] },
        { "PATCH", "/api/v1/projects/{id}", [Pm] },
        { "POST", "/api/v1/projects/{id}/timesheets", [Pm] },
        { "GET", "/api/v1/projects/{id}/timesheets", [Pm, Approver, Viewer] },
        { "POST", "/api/v1/timesheets/{id}/approve", [Approver] },
        { "POST", "/api/v1/timesheets/{id}/reject", [Approver] },
        { "POST", "/api/v1/projects/{id}/expenses", [Pm] },
        { "GET", "/api/v1/projects/{id}/expenses", [Pm, Approver, Viewer] },
        { "POST", "/api/v1/expenses/{id}/approve", [Approver] },
        { "POST", "/api/v1/expenses/{id}/reject", [Approver] },
        { "GET", "/api/v1/projects/{id}/reports/budget-vs-actual", [Pm, Approver, Viewer] },
        { "GET", "/api/v1/reports/portfolio-summary", [Pm, Approver, Viewer] },
    };

    public Task InitializeAsync() => factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public void Matrix_CoversEveryBusinessRouteTheApiExposes()
    {
        var exposed = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText?.StartsWith("api/v1", StringComparison.Ordinal) == true)
            .SelectMany(e => (e.Metadata.GetMetadata<Microsoft.AspNetCore.Routing.HttpMethodMetadata>()?.HttpMethods ?? []).Select(m =>
                $"{m} /{RouteParameter().Replace(e.RoutePattern.RawText!, "{id}")}"))
            .Order()
            .ToList();

        var documented = Endpoints.Select(row => $"{row[0]} {row[1]}").Order().ToList();

        Assert.Equal(14, documented.Count);
        Assert.Equal(documented, exposed);
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task EveryEndpoint_Returns401WithoutToken_403ForWrongRoles_AndAdmitsAllowedRoles(string method, string route, string[] allowedRoles)
    {
        var url = route.Replace("{id}", Guid.NewGuid().ToString());

        var anonymous = await SendAsync(factory.CreateClient(), method, url);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        foreach (var role in Roles.All)
        {
            var response = await SendAsync(factory.CreateClientAs($"{role.ToLowerInvariant()}-1", role), method, url);
            if (allowedRoles.Contains(role))
            {
                // Past authorization; may still be 400/404 because ids and bodies are fake.
                Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
                Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
            }
            else
            {
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            }
        }
    }

    [Fact] // I14
    public async Task Unauthorized_IsProblemJson_WithBearerChallenge()
    {
        var response = await factory.CreateClient().GetAsync("/api/v1/projects");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains(response.Headers.WwwAuthenticate, h => h.Scheme == "Bearer");
    }

    [Fact]
    public async Task BadTokens_AreAllRejectedWith401()
    {
        var now = DateTimeOffset.UtcNow;
        var otherKey = Convert.ToBase64String(Encoding.UTF8.GetBytes(new string('k', 48)));
        string[] roles = [Pm];

        var badTokens = new Dictionary<string, string>
        {
            ["expired an hour ago"] = DevTokens.Create(SiteLedgerApiFactory.Issuer, SiteLedgerApiFactory.Audience, factory.SigningKey, "pm-1", roles, TimeSpan.FromMinutes(5), now.AddHours(-1)),
            ["signed with another key"] = DevTokens.Create(SiteLedgerApiFactory.Issuer, SiteLedgerApiFactory.Audience, otherKey, "pm-1", roles, TimeSpan.FromMinutes(5), now),
            ["wrong audience"] = DevTokens.Create(SiteLedgerApiFactory.Issuer, "SomeOtherApi", factory.SigningKey, "pm-1", roles, TimeSpan.FromMinutes(5), now),
            ["wrong issuer"] = DevTokens.Create("Mallory", SiteLedgerApiFactory.Audience, factory.SigningKey, "pm-1", roles, TimeSpan.FromMinutes(5), now),
            ["unsigned (alg=none)"] = UnsignedToken(),
            ["garbage"] = "not.a.jwt",
        };

        foreach (var (name, token) in badTokens)
        {
            var client = factory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var response = await client.GetAsync("/api/v1/projects");
            Assert.True(response.StatusCode == HttpStatusCode.Unauthorized, $"{name}: expected 401, got {(int)response.StatusCode}");
        }
    }

    [Fact]
    public async Task ValidToken_WithUnknownRoleOrNoSubject_Is403()
    {
        var unknownRole = factory.CreateClientAs("someone", "Admin");
        var noSubject = factory.CreateClient();
        noSubject.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenWithoutSubject());

        Assert.Equal(HttpStatusCode.Forbidden, (await unknownRole.GetAsync("/api/v1/projects")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await noSubject.GetAsync("/api/v1/projects")).StatusCode);
    }

    [Fact]
    public async Task Actors_ComeFromTheToken_NeverFromTheRequestBody()
    {
        var pm = factory.CreateClientAs("pm-alice", Pm);
        var approver = factory.CreateClientAs("approver-bob", Approver);
        var project = await pm.CreateProjectAsync("CON-2026-001");
        var employee = await factory.AddEmployeeAsync(hourlyRateUsd: 55m);

        var submitted = await pm.PostAsJsonAsync($"/api/v1/projects/{project.Id}/timesheets", new
        {
            employeeId = employee.Id,
            workDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1),
            hours = 8m,
            submittedBySubject = "mallory", // spoofing attempt: must be ignored
        });
        var timesheet = await submitted.ReadAsync<TimesheetResponse>();
        var expense = await pm.SubmitExpenseAsync(project.Id);

        var approvedTimesheet = await (await approver.PostAsync($"/api/v1/timesheets/{timesheet.Id}/approve", content: null)).ReadAsync<TimesheetResponse>();
        var rejectedExpense = await (await approver.PostAsJsonAsync($"/api/v1/expenses/{expense.Id}/reject", new { reason = "Invoice missing" })).ReadAsync<ExpenseResponse>();

        Assert.Equal("pm-alice", timesheet.SubmittedBySubject);
        Assert.Equal("pm-alice", expense.SubmittedBySubject);
        Assert.Equal("approver-bob", approvedTimesheet.DecidedBySubject);
        Assert.Equal("approver-bob", rejectedExpense.DecidedBySubject);
        var auditActors = await factory.QueryDbAsync(db => db.ApprovalAudits.Select(a => a.ActorSubject).ToListAsync());
        Assert.Equal(["approver-bob", "approver-bob"], auditActors);
    }

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, string method, string url)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), url);
        if (method is "POST" or "PATCH")
        {
            request.Content = JsonContent.Create(new { });
        }

        return client.SendAsync(request);
    }

    private static string UnsignedToken()
    {
        static string Encode(string json) => Base64UrlEncoder.Encode(json);
        var exp = DateTimeOffset.UtcNow.AddMinutes(10).ToUnixTimeSeconds();
        return $"{Encode("""{"alg":"none","typ":"JWT"}""")}.{Encode($$"""{"sub":"pm-1","role":"ProjectManager","iss":"SiteLedger","aud":"SiteLedger.Api","exp":{{exp}}}""")}.";
    }

    private string TokenWithoutSubject() => new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
    {
        Issuer = SiteLedgerApiFactory.Issuer,
        Audience = SiteLedgerApiFactory.Audience,
        Expires = DateTime.UtcNow.AddMinutes(10),
        Claims = new Dictionary<string, object> { ["role"] = Viewer },
        SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(factory.SigningKey)), SecurityAlgorithms.HmacSha256),
    });

    [GeneratedRegex(@"\{[^}]+\}")]
    private static partial Regex RouteParameter();
}
