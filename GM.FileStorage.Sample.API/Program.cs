using GM.API.Startup;
using GM.FileStorage;
using GM.FileStorage.AzureBlob;
using GM.FileStorage.Local;
using GM.FileStorage.S3;
using GM.FileStorage.Sample.API;

// GM.API: Serilog, settings files + secrets (appsecrets.json / SECRETS_PROVIDER) layered into configuration.
var builder = ProgramExtension.CreateGMBuilder(args);

// GM.API: controllers, versioning, Swagger (from SwaggerDocOptions), CORS, validation, RFC 7807 errors,
// request logging (file bodies are described, never read or logged), localization, health checks.
builder.Services.ConfigureGMServices(builder.Configuration, "DefaultCors", "SwaggerDocOptions");

// All three backends are registered; FileStorage:Provider (default "Local") decides which one
// IFileStorageService resolves to — swapping storage is a config change, not a code change. The S3
// and Azure clients are only constructed when their provider is selected, so the sample runs on
// disk out of the box with no cloud config.
builder.Services.AddGMFileStorage(builder.Configuration);
builder.Services.AddGMLocalFileStorage(builder.Configuration);
builder.Services.AddGMS3FileStorage(builder.Configuration);
builder.Services.AddGMAzureBlobFileStorage(builder.Configuration);

// KYC-style tenant/user-scoped keys (replaces the default date-partitioned strategy).
builder.Services.AddSingleton<IFileKeyStrategy, TenantKeyStrategy>();

var app = builder.Build();

// Swagger UI, routing, CORS, logging, localization, exception handling, /health/live|ready|startup,
// and the controllers (Files/FilesController).
app.UseGMServices();

await app.RunAsync();

/// <summary>The app's entry point, exposed so the test project can boot it with WebApplicationFactory.</summary>
public partial class Program
{
    /// <summary>Only used as a <c>WebApplicationFactory&lt;Program&gt;</c> marker; never instantiated directly.</summary>
    protected Program() { }
}
