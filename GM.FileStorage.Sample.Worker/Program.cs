using GM.FileStorage.Sample.Infrastructure;
using GM.FileStorage.Sample.Persistence;
using GM.Secrets;
using Microsoft.Extensions.Hosting;

// The background side of the sample: runs GM.FileStorage.Catalog's jobs on GM.Scheduling. storage-cleanup (every
// minute) deletes the bytes uploads leave unreferenced (stored but never recorded, e.g. after a crash); retention and
// integrity-check purge long-deleted files and check stored bytes. The API only
// serves requests; the database migrations are applied by the API at startup.
var builder = Host.CreateApplicationBuilder(args);

// appsecrets.json (the connection string), the SECRETS_PROVIDER store and environment variables over appsettings.
builder.AddGMSecrets();

builder.Services.AddPersistence(builder.Configuration);
builder.Services.AddWorkerInfrastructure(builder.Configuration);

await builder.Build().RunAsync();
