using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Testcontainers.PostgreSql;
using TenderSync.Infrastructure;
using TenderSync.Infrastructure.Persistence;
using TenderSync.Infrastructure.Persistence.Seed;

namespace TenderSync.Infrastructure.Tests;

/// <summary>
/// One PostgreSQL 17 container per test collection. Applies the migrations and the seed once; tests create scopes from it.
/// Needs Docker. Tests using it carry [Trait("Category", "Integration")].
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    public const string AdminEmail = "admin@tendersync.test";
    public const string AdminPassword = "Admin-Password-123!";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("tendersync")
        .WithUsername("tendersync")
        .WithPassword("tendersync")
        .Build();

    private ServiceProvider? _services;

    public string ConnectionString => _container.GetConnectionString();

    public IServiceProvider Services => _services ?? throw new InvalidOperationException("Fixture not initialised.");

    public IServiceScope CreateScope() => Services.CreateScope();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        var services = new ServiceCollection();
        services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));
        services.AddInfrastructure(ConnectionString);
        services.Configure<SeedOptions>(options =>
        {
            options.AdminEmail = AdminEmail;
            options.AdminPassword = AdminPassword;
            options.AdminDisplayName = "Test Admin";
        });
        _services = services.BuildServiceProvider();

        using var scope = _services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await context.Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<DatabaseSeeder>().SeedAsync();
    }

    public async Task DisposeAsync()
    {
        if (_services is not null)
        {
            await _services.DisposeAsync();
        }

        await _container.DisposeAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}
