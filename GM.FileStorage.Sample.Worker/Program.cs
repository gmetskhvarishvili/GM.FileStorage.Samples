using GM.FileStorage.Sample.Infrastructure;
using GM.FileStorage.Sample.Persistence;
using GM.Secrets;
using Microsoft.Extensions.Hosting;

// The background side of the sample: runs StorageCleanupJob (GM.Scheduling, every minute), which deletes the bytes
// uploads leave unreferenced — abandoned uploads and replaced versions whose immediate delete failed. The API only
// serves requests; the database migrations are applied by the API at startup.
var builder = Host.CreateApplicationBuilder(args);

// appsecrets.json (the connection string), the SECRETS_PROVIDER store and environment variables over appsettings.
builder.AddGMSecrets();

builder.Services.AddPersistence(builder.Configuration);
builder.Services.AddWorkerInfrastructure(builder.Configuration);

await builder.Build().RunAsync();
