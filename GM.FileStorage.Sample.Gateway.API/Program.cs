using System;
using GM.Caching;
using GM.Caching.Redis;
using GM.DistributedLock;
using GM.DistributedLock.Redis;
using GM.Gateway;
using GM.HealthChecks;
using GM.RateLimiting.Http;
using GM.RateLimiting.Redis;
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
// The files API takes the caller's tenant and user from X-Tenant-Id / X-User-Id. Here they pass through as the
// client sent them, which is only fit for a demo: there is no sign-in yet. With sign-in, add
// .AddGMActorForwarding() (as GM.Identity's gateway does): it strips client copies and sets them from the token.
// ---------------------------------------------------------------------------------------------
builder.Services.AddGMGateway(
    builder.Configuration,
    configureHttp: options => options.DefaultPartition = RateLimitPartition.Ip);
if (redis)
    builder.Services.AddGMRedisRateLimitStore(); // atomic counts in Redis, on the connection AddGMRedisCaching opened

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

// /health/live, /health/ready, /health/startup.
app.MapGMHealthChecks();

// Reverse proxy pipeline: per-route rate limiting enforced at the edge before forwarding.
app.MapGMGateway();

await app.RunAsync();

// Exposes the implicit top-level Program class to the gateway tests (GmWebApplicationFactory<Program>).
public partial class Program;
