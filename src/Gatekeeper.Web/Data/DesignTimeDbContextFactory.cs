using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace Gatekeeper.Web.Data;

/// <summary>
/// Lets the EF Core command-line tools (migrations) build a context without spinning up the
/// whole web host. Runtime uses the factory registered in Program.cs; this is design-time only.
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<GatekeeperDbContext>
{
    public GatekeeperDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? "Host=127.0.0.1;Port=5432;Database=gatekeeper;Username=postgres";

        var options = new DbContextOptionsBuilder<GatekeeperDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new GatekeeperDbContext(options);
    }
}
