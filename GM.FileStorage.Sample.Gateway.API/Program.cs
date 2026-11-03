using System;
using GM.Caching;
using GM.DistributedLock;
using GM.Gateway;
using GM.HealthChecks;
using GM.RateLimiting.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------------------------
// Counter store + lock for edge rate limiting. In-memory suits one gateway instance; with several,
// use AddGMRedisCaching + AddGMRedisRateLimitStore so the limits are shared.
// ---------------------------------------------------------------------------------------------
builder.Services.AddGMCaching();
builder.Services.AddGMDistributedLock();

// ---------------------------------------------------------------------------------------------
// YARP reverse proxy + GM rate limiting, both from configuration (see appsettings.json): every route
// forwards to the files API; uploads get a tighter limit than reads. Bodies are streamed
// through, never buffered, so large uploads and ranged downloads (206) pass untouched.
// ---------------------------------------------------------------------------------------------
builder.Services.AddGMGateway(
    builder.Configuration,
    configureHttp: options => options.DefaultPartition = RateLimitPartition.Ip);

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
