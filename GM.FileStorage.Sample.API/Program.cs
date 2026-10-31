using GM.API.Startup;
using GM.FileStorage.Sample.Infrastructure;
using GM.FileStorage.Sample.Persistence;
using GM.FileStorage.Sample.Persistence.Context;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;

// GM.API: Serilog, settings files + secrets (appsecrets.json / SECRETS_PROVIDER) layered into configuration.
var builder = ProgramExtension.CreateGMBuilder(args);

// GM.API: controllers, versioning, Swagger (from SwaggerDocOptions), CORS, FluentValidation, GM.Mediator (handlers
// and validators discovered across the project assemblies), Mapster, RFC 7807 errors, request logging (file bodies
// are described, never read or logged), localization, health checks.
builder.Services.ConfigureGMServices(builder.Configuration, "DefaultCors", "SwaggerDocOptions");

// The catalog (stored files + their metadata rows) in PostgreSQL, and the storage backends + key strategy.
builder.Services.AddPersistence(builder.Configuration);
builder.Services.AddInfrastructure(builder.Configuration);

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
