using System;
using GM.Caching;
using GM.Caching.Redis;
using GM.DistributedLock;
using GM.DistributedLock.Redis;
using GM.Gateway;
using GM.HealthChecks;
using GM.Identity.Authorization;
using GM.RateLimiting.Http;
using GM.RateLimiting.Redis;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

// Kestrel:Limits from appsettings.json (the 60 MB request body cap, matching the files API). They aren't bound by
// default, which left Kestrel's 30 MB in force.
builder.Services.Configure<KestrelServerOptions>(builder.Configuration.GetSection("Kestrel"));

// ---------------------------------------------------------------------------------------------
// Counter store + lock for edge rate limiting: Redis when Redis:ConnectionString is set, so several
// gateway instances share one count per client; otherwise in-memory, which suits one instance.
// ---------------------------------------------------------------------------------------------
var redis = !string.IsNullOrWhiteSpace(builder.Configuration["Redis:ConnectionString"]);

// Sign-in through GM.Identity (Authentication:Enabled, on by default). Its tokens are opaque: the gateway looks each one
// up in GM.Identity's session store, a Redis, so Redis:ConnectionString must point at the one GM.Identity writes to.
var signIn = builder.Configuration.GetValue("Authentication:Enabled", true);
if (signIn && !redis)
    throw new InvalidOperationException(
        "Sign-in needs Redis:ConnectionString: GM.Identity's session store, where the gateway checks tokens. " +
        "Set it, or turn sign-in off with Authentication:Enabled=false.");
if (redis)
{
    builder.Services.AddGMRedisCaching(builder.Configuration);
    builder.Services.AddGMRedisDistributedLock(builder.Configuration);
}
else
{
    builder.Services.AddGMCaching();
    builder.Services.AddGMDistributedLock();
}

// ---------------------------------------------------------------------------------------------
// YARP reverse proxy + GM rate limiting, both from configuration (see appsettings.json): every route
// forwards to the files API; uploads get a tighter limit than reads. Bodies are streamed
// through, never buffered, so large uploads and ranged downloads (206) pass untouched.
//
// The files API takes the caller's tenant and user from X-Tenant-Id / X-User-Id. With sign-in on, the gateway sets
// them from the token (AddGMActorForwarding) after stripping any copies the client sent, so they can't be spoofed;
// the API trusts them because only the gateway reaches it. With sign-in off they pass through as sent, which only
// suits local development.
// ---------------------------------------------------------------------------------------------
var proxy = builder.Services.AddGMGateway(
    builder.Configuration,
    configureHttp: options => options.DefaultPartition = RateLimitPartition.Ip);
if (signIn)
    proxy.AddGMActorForwarding();
if (redis)
    builder.Services.AddGMRedisRateLimitStore(); // atomic counts in Redis, on the connection AddGMRedisCaching opened

// ---------------------------------------------------------------------------------------------
// Authentication: a GM.Identity bearer token, checked against its session in Redis. The file routes carry
// AuthorizationPolicy "default" (appsettings.json), so they need one: none or an unknown one is 401, and a token
// asking for a tenant (X-Tenant-Id) its session doesn't belong to is 403. Health and the API docs stay open.
// A session without a tenant may name one in X-Tenant-Id, or work in the shared "public" space without it.
// ---------------------------------------------------------------------------------------------
if (signIn)
{
    builder.Services.AddAuthentication(OpaqueTokenAuthenticationHandler.SchemeName)
        .AddScheme<AuthenticationSchemeOptions, OpaqueTokenAuthenticationHandler>(OpaqueTokenAuthenticationHandler.SchemeName, null);
    builder.Services.AddAuthorization();
}
else
{
    // Sign-in off: no token is checked, and the routes' "default" policy lets everyone through.
    builder.Services.AddAuthorization(options =>
        options.DefaultPolicy = new AuthorizationPolicyBuilder().RequireAssertion(_ => true).Build());
}

// ---------------------------------------------------------------------------------------------
// Health: liveness is the gateway itself; readiness also needs the files API to be up.
// ---------------------------------------------------------------------------------------------
var filesApi = builder.Configuration["ReverseProxy:Clusters:files-api:Destinations:primary:Address"]
    ?? throw new InvalidOperationException("ReverseProxy:Clusters:files-api:Destinations:primary:Address is required.");
builder.Services.AddGMHealthChecks()
    .AddGMHttpCheck("files-api", new Uri(new Uri(filesApi), "health/live"), System.Net.Http.HttpMethod.Get,
        tags: [HealthTags.Readiness]);

var app = builder.Build();

app.UseRouting();

// Swagger UI at the root, rendering the files API's OpenAPI document proxied through the gateway (the
// "files-api-docs" route). "Try it out" calls go back through the gateway, so the edge limits apply.
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "Files API v1");
    options.RoutePrefix = string.Empty;
    options.DocumentTitle = "GM FileStorage Gateway";
});

if (signIn)
    app.UseAuthentication();
app.UseAuthorization();

// /health/live, /health/ready, /health/startup.
app.MapGMHealthChecks();

// Reverse proxy pipeline: per-route rate limiting enforced at the edge before forwarding.
app.MapGMGateway();

await app.RunAsync();

// Exposes the implicit top-level Program class to the gateway tests (GmWebApplicationFactory<Program>).
public partial class Program;
