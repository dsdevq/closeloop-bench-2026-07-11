using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Infrastructure;

public class CrmDbContextFactory : IDesignTimeDbContextFactory<CrmDbContext>
{
    public CrmDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? "Host=localhost;Port=5432;Database=closeloop_dev;Username=closeloop;Password=changeme";

        var options = new DbContextOptionsBuilder<CrmDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new CrmDbContext(options);
    }
}
