using GM.API.Startup;
using GM.FileStorage.Sample.API.Files;
using GM.FileStorage.Sample.API.Files.Multipart;
using GM.FileStorage.Sample.Infrastructure;
using GM.FileStorage.Sample.Persistence;
using GM.FileStorage.Sample.Persistence.Context;
using GM.Idempotency;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Swashbuckle.AspNetCore.SwaggerGen;
using System;
using System.Linq;

// GM.API: Serilog, settings files + secrets (appsecrets.json / SECRETS_PROVIDER) layered into configuration.
var builder = ProgramExtension.CreateGMBuilder(args);

// GM.API: controllers, versioning, Swagger (from SwaggerDocOptions), CORS, FluentValidation, GM.Mediator (handlers
// and validators discovered across the project assemblies), Mapster, RFC 7807 errors, request logging (file bodies
// are described, never read or logged), localization, health checks.
builder.Services.ConfigureGMServices(builder.Configuration, "DefaultCors", "SwaggerDocOptions");

// Kestrel:Limits from appsettings.json (the 60 MB request body cap). GM's builder doesn't bind them, which left
// Kestrel's 30 MB default in force.
builder.Services.Configure<KestrelServerOptions>(builder.Configuration.GetSection("Kestrel"));

// Uploads are read straight off the request body (no IFormFile), so Swagger is told about their file fields.
builder.Services.Configure<SwaggerGenOptions>(options => options.OperationFilter<MultipartUploadOperationFilter>());

// The catalog (stored files + their metadata rows) in PostgreSQL, and the storage backends + key strategy.
builder.Services.AddPersistence(builder.Configuration);
builder.Services.AddInfrastructure(builder.Configuration);

// Counts uploaded files per client (RateLimiting:Policies:uploaded-files).
builder.Services.AddScoped<UploadQuota>();

// GM.API replays a POST that repeats its Idempotency-Key. The key is locked while the first request runs, and an
// upload can run for minutes, so Idempotency:LockExpiry outlasts it: a retry meanwhile gets 409, never a second upload.
builder.Services.Configure<IdempotencyOptions>(builder.Configuration.GetSection("Idempotency"));

var app = builder.Build();

// Swagger UI, routing, CORS, logging, localization, exception handling, /health/live|ready|startup,
// and the controllers (Files/FilesController).
app.UseGMServices();

using (var scope = app.Services.CreateScope())
{
    try
    {
        var context = scope.ServiceProvider.GetService<ApplicationDbContext>();
        if (context != null && (await context.Database.GetPendingMigrationsAsync()).Any())
        {
            await context.Database.MigrateAsync();
        }
    }
    catch (Exception ex)
    {
        var logger = app.Services.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "An error occurred while migrating the database.");
    }
}

await app.RunAsync();

// Exposes the implicit top-level Program class to the test project (GmWebApplicationFactory<Program>).
public partial class Program;
