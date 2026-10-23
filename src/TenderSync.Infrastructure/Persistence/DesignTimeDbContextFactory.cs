using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TenderSync.Infrastructure.Persistence;

/// <summary>
/// Used only by `dotnet ef` at design time. Reads TENDERSYNC_CONNECTION or falls back to the Compose default.
/// No connection is opened for `migrations add`; one is needed for `database update`.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public const string DefaultConnectionString = "Host=localhost;Port=5432;Database=tendersync;Username=tendersync;Password=tendersync";

    public AppDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("TENDERSYNC_CONNECTION") ?? DefaultConnectionString;
        var options = new DbContextOptionsBuilder<AppDbContext>();
        DependencyInjection.ConfigureDbContext(options, connectionString);
        return new AppDbContext(options.Options);
    }
}
