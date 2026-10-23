using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using TenderSync.Application.Abstractions;
using TenderSync.Application.Abstractions.Persistence;
using TenderSync.Infrastructure.Identity;
using TenderSync.Infrastructure.Persistence;
using TenderSync.Infrastructure.Persistence.Interceptors;
using TenderSync.Infrastructure.Persistence.Seed;
using TenderSync.Infrastructure.Services;

namespace TenderSync.Infrastructure;

public static class DependencyInjection
{
    public const string ConnectionStringName = "TenderSync";

    /// <summary>Registers persistence (EF Core + Dapper), Identity stores, the clock, the municipality lookup and the seeder.</summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName)
            ?? throw new InvalidOperationException($"Connection string '{ConnectionStringName}' is not configured.");

        return services.AddInfrastructure(connectionString, configuration);
    }

    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string connectionString, IConfiguration? configuration = null)
    {
        services.AddSingleton(_ => new NpgsqlDataSourceBuilder(connectionString).Build());
        services.AddSingleton<IDateTimeProvider, SystemDateTimeProvider>();
        services.AddSingleton<IMunicipalityLookup, CachedMunicipalityLookup>();
        services.AddScoped<AuditAndOutboxInterceptor>();

        services.AddDbContext<AppDbContext>((provider, options) =>
        {
            ConfigureDbContext(options, provider.GetRequiredService<NpgsqlDataSource>());
            options.AddInterceptors(provider.GetRequiredService<AuditAndOutboxInterceptor>());
        });

        services.AddScoped<IAppDbContext>(provider => provider.GetRequiredService<AppDbContext>());
        services.AddScoped<IReadDb, DapperReadDb>();
        services.AddScoped<DatabaseSeeder>();

        if (configuration is not null)
        {
            services.Configure<SeedOptions>(configuration.GetSection(SeedOptions.SectionName));
        }
        else
        {
            services.AddOptions<SeedOptions>();
        }

        services.AddIdentityCore<ApplicationUser>(identity =>
            {
                identity.User.RequireUniqueEmail = true;
                identity.Password.RequiredLength = 12;
                identity.Password.RequireNonAlphanumeric = false;
                identity.Lockout.MaxFailedAccessAttempts = 5;
                identity.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            })
            .AddRoles<ApplicationRole>()
            .AddEntityFrameworkStores<AppDbContext>();

        // Token providers (password reset, email confirmation) are added by the API host in WP02, which has the ASP.NET Core framework reference.

        return services;
    }

    /// <summary>Shared by runtime registration and the design-time factory so migrations see the same model.</summary>
    public static void ConfigureDbContext(DbContextOptionsBuilder options, NpgsqlDataSource dataSource)
    {
        options.UseNpgsql(dataSource, ConfigureNpgsql).UseSnakeCaseNamingConvention();
    }

    public static void ConfigureDbContext(DbContextOptionsBuilder options, string connectionString)
    {
        options.UseNpgsql(connectionString, ConfigureNpgsql).UseSnakeCaseNamingConvention();
    }

    private static void ConfigureNpgsql(Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure.NpgsqlDbContextOptionsBuilder npgsql)
    {
        npgsql.MigrationsAssembly(typeof(DependencyInjection).Assembly.GetName().Name);
        npgsql.MigrationsHistoryTable(AppDbContext.MigrationsHistoryTable, AppDbContext.Schema);
        npgsql.EnableRetryOnFailure(maxRetryCount: 3);
    }
}
