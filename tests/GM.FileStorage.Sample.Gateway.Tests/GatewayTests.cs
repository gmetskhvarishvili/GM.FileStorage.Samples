using GM.Testing.AspNetCore;
using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace GM.FileStorage.Sample.Gateway.Tests;

/// <summary>
/// The gateway as configured in its appsettings.json — routes, per-IP rate limits, health, Swagger UI — in front of
/// <see cref="StubFilesApi"/> instead of the real files API, with sign-in off and no Redis (in-memory rate limits);
/// <see cref="GatewaySignInTests"/> covers sign-in. Each test gets a fresh gateway, so rate-limit counters start at zero.
/// </summary>
[Collection(GatewayEnvironment.Name)]
public sealed class GatewayTests : IAsyncLifetime
{
    // Read when the gateway registers its services, so it's set before the host is built (the gateway's appsettings
    // point at https://localhost:7140).
    private const string ApiAddressVariable = "ReverseProxy__Clusters__files-api__Destinations__primary__Address";
    private const string SignInVariable = "Authentication__Enabled";

    private readonly StubFilesApi _api = new();
    private GmWebApplicationFactory<Program> _gateway = null!;

    public async Task InitializeAsync()
    {
        await _api.StartAsync();
        Environment.SetEnvironmentVariable(ApiAddressVariable, _api.Address.ToString());
        Environment.SetEnvironmentVariable(SignInVariable, "false");
        _gateway = new GmWebApplicationFactory<Program>();
    }

    public async Task DisposeAsync()
    {
        await _gateway.DisposeAsync();
        await _api.DisposeAsync();
        Environment.SetEnvironmentVariable(ApiAddressVariable, null);
        Environment.SetEnvironmentVariable(SignInVariable, null);
    }

    private static MultipartFormDataContent PdfForm(string field = "file")
    {
        var form = new MultipartFormDataContent();
        var part = new ByteArrayContent(Encoding.UTF8.GetBytes("%PDF-1.7 x"));
        part.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(part, field, "a.pdf");
        return form;
    }

    [Fact]
    public async Task File_requests_are_forwarded_to_the_api_as_sent()
    {
        using var client = _gateway.CreateClient();
        // Sign-in off, so the caller's tenant passes through as sent (see the gateway's Program.cs).
        var tenantId = Guid.NewGuid().ToString();
        client.DefaultRequestHeaders.Add("X-Tenant-Id", tenantId);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/files?contentType=application%2Fpdf")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/api/v1/files", PdfForm())).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/api/v1/files/batch", PdfForm("files"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.DeleteAsync("/api/v1/files/6f1e0000-0000-0000-0000-000000000001")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/swagger/v1/swagger.json")).StatusCode);

        // Range requests pass through both ways: the header in, the 206 out.
        using var ranged = new HttpRequestMessage(HttpMethod.Get, "/api/v1/files/6f1e0000-0000-0000-0000-000000000001/content");
        ranged.Headers.Range = new RangeHeaderValue(0, 99);
        Assert.Equal(HttpStatusCode.PartialContent, (await client.SendAsync(ranged)).StatusCode);

        var received = _api.Requests.ToArray();
        Assert.Equal(
            [
                "GET /api/v1/files?contentType=application%2Fpdf",
                "POST /api/v1/files",
                "POST /api/v1/files/batch",
                "DELETE /api/v1/files/6f1e0000-0000-0000-0000-000000000001",
                "GET /swagger/v1/swagger.json",
                "GET /api/v1/files/6f1e0000-0000-0000-0000-000000000001/content",
            ],
            received.Select(r => $"{r.Method} {r.PathAndQuery}"));
        // Upload bodies arrive whole.
        Assert.All(received.Where(r => r.Method == "POST"), r => Assert.True(r.BodyLength > 0));
        Assert.Equal("bytes=0-99", received[^1].Range);
        Assert.All(received, r => Assert.Equal(tenantId, r.TenantId));
    }

    [Theory]
    [InlineData("GET", "/admin", HttpStatusCode.NotFound)]
    [InlineData("GET", "/files/documents/9f/9f1c.pdf", HttpStatusCode.NotFound)]
    [InlineData("GET", "/api/v1/users", HttpStatusCode.NotFound)]
    // The docs route matches the path but only for GET.
    [InlineData("POST", "/swagger/v1/swagger.json", HttpStatusCode.MethodNotAllowed)]
    public async Task Anything_else_is_not_forwarded(string method, string path, HttpStatusCode expected)
    {
        using var client = _gateway.CreateClient();

        var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), path));

        Assert.Equal(expected, response.StatusCode);
        Assert.Empty(_api.Requests);
    }

    [Fact]
    public async Task Uploads_are_limited_to_30_a_minute_and_reads_have_their_own_limit()
    {
        using var client = _gateway.CreateClient();

        for (var i = 0; i < 30; i++)
        {
            var allowed = await client.PostAsync("/api/v1/files", PdfForm());
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
            Assert.Equal("30", allowed.Headers.GetValues("X-RateLimit-Limit").Single());
        }

        // The 31st upload, single or batch, is refused at the edge and never reaches the API.
        var refused = await client.PostAsync("/api/v1/files", PdfForm());
        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        Assert.True(refused.Headers.Contains("Retry-After"));
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.PostAsync("/api/v1/files/batch", PdfForm("files"))).StatusCode);
        Assert.Equal(30, _api.Requests.Count);

        // Reads count against the separate 300-a-minute policy, so they still go through.
        var read = await client.GetAsync("/api/v1/files");
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.Equal("300", read.Headers.GetValues("X-RateLimit-Limit").Single());
    }

    [Fact]
    public async Task Readiness_follows_the_api_and_liveness_does_not()
    {
        using var client = _gateway.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/ready")).StatusCode);

        await _api.StopAsync();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/health/ready")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
    }

    [Fact]
    public async Task Swagger_ui_is_served_by_the_gateway_itself()
    {
        using var client = _gateway.CreateClient();

        var page = await client.GetAsync("/index.html");

        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains("GM FileStorage Gateway", await page.Content.ReadAsStringAsync());
        Assert.Empty(_api.Requests);
    }
}
