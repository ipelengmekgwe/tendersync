using Microsoft.EntityFrameworkCore;
using TenderSync.Infrastructure.Persistence;
using TenderSync.Infrastructure.Persistence.Seed;

namespace TenderSync.Api.Extensions;

public static class StartupExtensions
{
    /// <summary>
    /// Applies pending migrations and seeds reference data when Database:MigrateOnStartup / Database:SeedOnStartup are true
    /// (Development default). Production runs migrations from a one-shot job (PLAN.md §15).
    /// </summary>
    public static async Task MigrateAndSeedAsync(this WebApplication app)
    {
        var migrate = app.Configuration.GetValue("Database:MigrateOnStartup", false);
        var seed = app.Configuration.GetValue("Database:SeedOnStartup", false);
        if (!migrate && !seed)
        {
            return;
        }

        using var scope = app.Services.CreateScope();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");

        if (migrate)
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var pending = (await context.Database.GetPendingMigrationsAsync()).ToList();
            if (pending.Count > 0)
            {
                logger.LogInformation("Applying {Count} pending migration(s): {Migrations}", pending.Count, string.Join(", ", pending));
                await context.Database.MigrateAsync();
            }
        }

        if (seed)
        {
            await scope.ServiceProvider.GetRequiredService<DatabaseSeeder>().SeedAsync();
        }
    }
}
