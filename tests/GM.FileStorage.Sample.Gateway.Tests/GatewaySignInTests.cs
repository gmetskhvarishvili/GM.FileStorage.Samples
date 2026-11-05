using GM.Identity;
using GM.Identity.Authorization;
using GM.Testing.AspNetCore;
using StackExchange.Redis;
using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace GM.FileStorage.Sample.Gateway.Tests;

/// <summary>
/// Sign-in as the gateway runs it by default: the file routes need a GM.Identity bearer token, which the gateway looks up
/// in GM.Identity's session store, and the caller it names reaches the API as X-User-Id / X-Tenant-Id / X-Session-Id.
/// Sessions are written straight into Redis here, as GM.Identity writes them on sign-in.
/// </summary>
/// <remarks>
/// Needs a Redis: <c>GM_FILESTORAGE_TEST_REDIS</c> (host:port). Without it these tests pass without running; CI sets
/// it, and <c>GM_FILESTORAGE_TEST_REQUIRE_LIVE=true</c> makes a missing one fail instead.
/// </remarks>
[Collection(GatewayEnvironment.Name)]
public sealed class GatewaySignInTests : IAsyncLifetime
{
    private const string ApiAddressVariable = "ReverseProxy__Clusters__files-api__Destinations__primary__Address";
    private const string RedisVariable = "Redis__ConnectionString";
    private const string FileContent = "/api/v1/files/6f1e0000-0000-0000-0000-000000000001/content";

    private static readonly JsonSerializerOptions SessionJson = new(JsonSerializerDefaults.Web);

    private readonly string? _redisAddress = Environment.GetEnvironmentVariable("GM_FILESTORAGE_TEST_REDIS");
    private readonly StubFilesApi _api = new();
    private GmWebApplicationFactory<Program> _gateway = null!;
    private ConnectionMultiplexer? _redis;

    private bool Skipped => _redis is null;

    public async Task InitializeAsync()
    {
        if (string.IsNullOrWhiteSpace(_redisAddress))
        {
            Assert.False(Environment.GetEnvironmentVariable("GM_FILESTORAGE_TEST_REQUIRE_LIVE") == "true",
                "GM_FILESTORAGE_TEST_REDIS is required when GM_FILESTORAGE_TEST_REQUIRE_LIVE is true.");
            return;
        }

        _redis = await ConnectionMultiplexer.ConnectAsync(_redisAddress);
        await _api.StartAsync();
        Environment.SetEnvironmentVariable(ApiAddressVariable, _api.Address.ToString());
        Environment.SetEnvironmentVariable(RedisVariable, _redisAddress);
        _gateway = new GmWebApplicationFactory<Program>();
    }

    public async Task DisposeAsync()
    {
        if (Skipped)
            return;
        await _gateway.DisposeAsync();
        await _api.DisposeAsync();
        await _redis!.DisposeAsync();
        Environment.SetEnvironmentVariable(ApiAddressVariable, null);
        Environment.SetEnvironmentVariable(RedisVariable, null);
    }

    // A session as GM.Identity stores it on sign-in, under the hash of the token it hands out.
    private async Task<string> SignInAsync(Guid? userId, Guid? tenantId, DateTime? expiresAt = null)
    {
        var token = TokenGenerator.Generate();
        var session = new SessionInfo(userId, Guid.NewGuid(), Guid.NewGuid(), expiresAt ?? DateTime.UtcNow.AddMinutes(5), tenantId);
        await _redis!.GetDatabase().StringSetAsync(
            "gm-identity:auth:session:" + TokenGenerator.Hash(token), JsonSerializer.Serialize(session, SessionJson), TimeSpan.FromMinutes(5));
        return token;
    }

    private HttpClient Client(string? token = null)
    {
        var client = _gateway.CreateClient();
        if (token is not null)
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [Fact]
    public async Task Without_a_valid_token_file_routes_are_401_and_never_reach_the_api()
    {
        if (Skipped)
            return;

        using var anonymous = Client();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v1/files")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(FileContent)).StatusCode);
        using var unknown = Client(TokenGenerator.Generate());
        Assert.Equal(HttpStatusCode.Unauthorized, (await unknown.GetAsync("/api/v1/files")).StatusCode);
        using var expired = Client(await SignInAsync(Guid.NewGuid(), null, DateTime.UtcNow.AddMinutes(-1)));
        Assert.Equal(HttpStatusCode.Unauthorized, (await expired.GetAsync("/api/v1/files")).StatusCode);

        Assert.Empty(_api.Requests);

        // Health and the API docs stay open.
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync("/health/live")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync("/swagger/v1/swagger.json")).StatusCode);
    }

    [Fact]
    public async Task The_signed_in_caller_reaches_the_api_and_spoofed_headers_are_replaced()
    {
        if (Skipped)
            return;
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        using var client = Client(await SignInAsync(userId, tenantId));

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/files");
        request.Headers.Add("X-User-Id", Guid.NewGuid().ToString());
        request.Headers.Add("X-Session-Id", Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(request)).StatusCode);

        var received = Assert.Single(_api.Requests);
        Assert.Equal(userId.ToString(), received.UserId);
        Assert.Equal(tenantId.ToString(), received.TenantId);
        Assert.NotNull(received.ClientId);
        Assert.NotNull(received.SessionId);
    }

    [Fact]
    public async Task A_tenant_session_naming_another_tenant_is_403()
    {
        if (Skipped)
            return;
        using var client = Client(await SignInAsync(Guid.NewGuid(), Guid.NewGuid()));

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/files");
        request.Headers.Add("X-Tenant-Id", Guid.NewGuid().ToString());

        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(request)).StatusCode);
        Assert.Empty(_api.Requests);
    }

    [Fact]
    public async Task A_session_without_a_tenant_picks_one_or_works_in_the_public_space()
    {
        if (Skipped)
            return;
        var tenantId = Guid.NewGuid().ToString();
        using var client = Client(await SignInAsync(Guid.NewGuid(), null));

        using var inTenant = new HttpRequestMessage(HttpMethod.Get, "/api/v1/files");
        inTenant.Headers.Add("X-Tenant-Id", tenantId);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(inTenant)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/files")).StatusCode);

        Assert.Equal([tenantId, null], _api.Requests.Select(r => r.TenantId));
    }

    [Fact]
    public void Sign_in_without_redis_refuses_to_start()
    {
        if (Skipped)
            return;
        Environment.SetEnvironmentVariable(RedisVariable, null);
        using var gateway = new GmWebApplicationFactory<Program>();

        var error = Assert.ThrowsAny<Exception>(() => gateway.CreateClient());
        Assert.Contains("Redis:ConnectionString", error.ToString(), StringComparison.Ordinal);
    }
}
