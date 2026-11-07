using GM.FileStorage.Sample.Infrastructure;
using GM.FileStorage.Sample.Persistence;
using GM.Secrets;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.Hosting;
using System.Linq;

// The background side of the sample: runs StorageCleanupJob (GM.Scheduling, every minute), which deletes the bytes
// uploads leave unreferenced (stored but never recorded, e.g. after a crash). The API only
// serves requests; the database migrations are applied by the API at startup.
var builder = Host.CreateApplicationBuilder(args);

// appsettings.Local.json (git-ignored): this machine's settings, such as the storage provider and where files go.
// Right after appsettings.json, so the environment's file and environment variables still win, as for the API
// (GM.API's builder loads it there).
var appsettings = builder.Configuration.Sources.ToList().FindIndex(s => s is JsonConfigurationSource { Path: "appsettings.json" });
builder.Configuration.Sources.Insert(appsettings + 1, new JsonConfigurationSource
{
    Path = "appsettings.Local.json",
    Optional = true,
    ReloadOnChange = true,
    FileProvider = builder.Environment.ContentRootFileProvider,
});

// appsecrets.json (the connection string), the SECRETS_PROVIDER store and environment variables over appsettings.
builder.AddGMSecrets();

builder.Services.AddPersistence(builder.Configuration);
builder.Services.AddWorkerInfrastructure(builder.Configuration);

await builder.Build().RunAsync();
