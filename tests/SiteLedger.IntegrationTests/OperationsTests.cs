using System.Net;
using System.Text.Json;
using SiteLedger.IntegrationTests.Fixtures;

namespace SiteLedger.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class OperationsTests(SiteLedgerApiFactory factory)
{
    private const string Header = "X-Correlation-ID";

    [Fact]
    public async Task HealthLive_IsAnonymous_AndDoesNotNeedTheDatabase()
    {
        var response = await factory.CreateClient().GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task HealthReady_IsAnonymous_ChecksSqlServer_AndRevealsNoDetails()
    {
        var response = await factory.CreateClient().GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync()); // no server names, versions or errors
    }

    [Fact]
    public async Task CorrelationId_IsGeneratedWhenMissing_AndEchoedWhenSafe()
    {
        var client = factory.CreateClient();

        var generated = await client.GetAsync("/health/live");
        var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        request.Headers.Add(Header, "client-req-42");
        var echoed = await client.SendAsync(request);

        Assert.Matches("^[0-9a-f]{32}$", Assert.Single(generated.Headers.GetValues(Header)));
        Assert.Equal("client-req-42", Assert.Single(echoed.Headers.GetValues(Header)));
    }

    [Theory]
    [InlineData("has spaces")]
    [InlineData("<script>")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")] // 65 chars
    public async Task CorrelationId_UnsafeIncomingValue_IsReplaced(string unsafeValue)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        request.Headers.TryAddWithoutValidation(Header, unsafeValue);

        var response = await factory.CreateClient().SendAsync(request);

        Assert.Matches("^[0-9a-f]{32}$", Assert.Single(response.Headers.GetValues(Header)));
    }

    [Fact]
    public async Task ErrorResponses_CarryTheSameCorrelationIdInHeaderAndBody()
    {
        var response = await factory.CreateTrustedClient().GetAsync($"/api/v1/projects/{Guid.NewGuid()}");
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(Assert.Single(response.Headers.GetValues(Header)), body.RootElement.GetProperty("correlationId").GetString());
    }
}
