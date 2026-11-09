using GM.FileStorage.Sample.Persistence.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace GM.FileStorage.Sample.Persistence.Context;

public class ApplicationDbContextFactory : DesignTimeDbContextFactoryBase<ApplicationDbContext>
{
    protected override ApplicationDbContext CreateNewInstance(DbContextOptions<ApplicationDbContext> options) =>
        new(options);
}
