using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace GM.FileStorage.Sample.Gateway.Tests;

/// <summary>
/// Stands in for the files API on a real local port, so the gateway proxies to it over HTTP as it would to the API.
/// Records every request it receives and answers with what it saw.
/// </summary>
public sealed class StubFilesApi : IAsyncDisposable
{
    private WebApplication? _app;

    /// <summary>One request the stub received.</summary>
    public sealed record Received(string Method, string PathAndQuery, long BodyLength, string? Range, string? TenantId);

    public ConcurrentQueue<Received> Requests { get; } = new();

    public Uri Address { get; private set; } = null!;

    public async Task StartAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        _app = builder.Build();

        // Readiness of the gateway probes this.
        _app.MapGet("/health/live", () => Results.Ok("live"));

        _app.Run(async context =>
        {
            using var body = new MemoryStream();
            await context.Request.Body.CopyToAsync(body);
            var range = context.Request.Headers.Range.FirstOrDefault();
            var received = new Received(context.Request.Method, context.Request.Path + context.Request.QueryString, body.Length, range,
                context.Request.Headers["X-Tenant-Id"].FirstOrDefault());
            Requests.Enqueue(received);

            // A ranged request gets a 206, as the API answers one.
            context.Response.StatusCode = range is null ? StatusCodes.Status200OK : StatusCodes.Status206PartialContent;
            await context.Response.WriteAsJsonAsync(received);
        });

        await _app.StartAsync();
        var address = _app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        Address = new Uri(address.EndsWith('/') ? address : address + "/");
    }

    public async Task StopAsync()
    {
        if (_app != null)
            await _app.StopAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_app != null)
            await _app.DisposeAsync();
    }
}
