using GM.FileStorage.Sample.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

using System;
using System.IO;

namespace GM.FileStorage.Sample.Persistence.Infrastructure;

public abstract class DesignTimeDbContextFactoryBase<TContext> :
    IDesignTimeDbContextFactory<TContext> where TContext : ApplicationDbContext
{
    private const string ConnectionStringName = "ApplicationDatabase";
    private const string AspNetCoreEnvironment = "ASPNETCORE_ENVIRONMENT";

    public TContext CreateDbContext(string[] args)
    {
        var basePath = Path.Combine(Directory.GetCurrentDirectory(), "..", "GM.FileStorage.Sample.API");
        return Create(basePath, Environment.GetEnvironmentVariable(AspNetCoreEnvironment));
    }

    protected abstract TContext CreateNewInstance(DbContextOptions<TContext> options);

    private TContext Create(string basePath, string? environmentName)
    {
        // The same sources the API host reads, so `dotnet ef` finds the password wherever the app does: the
        // committed appsettings.json carries none; the gitignored appsecrets.json or the environment supplies it.
        var configuration = new ConfigurationBuilder()
            .SetBasePath(basePath)
            .AddJsonFile("appsettings.json")
            .AddJsonFile($"appsettings.Local.json", optional: true)
            .AddJsonFile($"appsettings.{environmentName}.json", optional: true)
            .AddJsonFile("appsecrets.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString(ConnectionStringName);

        return Create(connectionString);
    }

    private TContext Create(string? connectionString)
    {
        if (string.IsNullOrEmpty(connectionString))
        {
            throw new ArgumentException($"Connection string '{ConnectionStringName}' is null or empty.", nameof(connectionString));
        }

        var optionsBuilder = new DbContextOptionsBuilder<TContext>();

        // Must match the runtime options (DependencyInjection.AddPersistence), or migrations are generated for a
        // different model than the app queries — here, PascalCase columns while the app expects snake_case.
        optionsBuilder.UseNpgsql(connectionString).UseSnakeCaseNamingConvention();

        return CreateNewInstance(optionsBuilder.Options);
    }
}
