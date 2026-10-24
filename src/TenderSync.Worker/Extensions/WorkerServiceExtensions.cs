using System.Text;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using TenderSync.Application.Abstractions;
using TenderSync.Application.Abstractions.Jobs;
using TenderSync.Application.Security;
using TenderSync.Infrastructure.Identity;
using TenderSync.Infrastructure.Persistence;
using TenderSync.Infrastructure.Storage;
using TenderSync.Worker.Infrastructure;
using TenderSync.Worker.Jobs;

namespace TenderSync.Worker.Extensions;

public static class WorkerServiceExtensions
{
    public const string HangfireSchema = "hangfire";
    public const string AdminPolicy = "Admin";

    /// <summary>Hangfire storage and server, the system identity, JWT auth for the dashboard, health checks and job classes.</summary>
    public static IServiceCollection AddWorkerServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<ICurrentUser, WorkerCurrentUser>();
        services.AddProblemDetails();

        services.AddHangfire((provider, config) => config
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UsePostgreSqlStorage(
                // Read from configuration, not NpgsqlDataSource.ConnectionString: the data source strips the password.
                provider.GetRequiredService<IConfiguration>().GetConnectionString(TenderSync.Infrastructure.DependencyInjection.ConnectionStringName)
                    ?? throw new InvalidOperationException("Connection string 'TenderSync' is not configured."),
                new PostgreSqlStorageOptions
                {
                    SchemaName = HangfireSchema,
                    PrepareSchemaIfNecessary = true,
                    QueuePollInterval = TimeSpan.FromSeconds(configuration.GetValue("Hangfire:QueuePollSeconds", 5)),
                }));

        // Servers start in HangfireServersHostedService, which reads Hangfire:ServerEnabled / WorkerCount / PipelineWorkerCount
        // when the host starts, so WebApplicationFactory overrides apply (an eager read here would ignore them).
        services.AddHostedService<HangfireServersHostedService>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                var jwt = configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwt.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwt.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    RoleClaimType = JwtClaims.Role,
                    NameClaimType = JwtClaims.Name,
                };
                options.Events = new JwtBearerEvents
                {
                    // Browsers cannot send a bearer header for the dashboard, so accept the cookie set by DashboardTokenCookieMiddleware.
                    OnMessageReceived = context =>
                    {
                        if (string.IsNullOrEmpty(context.Token) && context.Request.Cookies.TryGetValue(DashboardTokenCookieMiddleware.CookieName, out var cookie))
                        {
                            context.Token = cookie;
                        }

                        return Task.CompletedTask;
                    },
                };
            });

        services.AddAuthorizationBuilder()
            .AddPolicy(AdminPolicy, policy => policy.RequireRole(RoleHierarchy.RolesAtOrAbove(Roles.Admin)));

        services.AddHealthChecks()
            .AddDbContextCheck<AppDbContext>("database", tags: ["ready"])
            .AddCheck<HangfireStorageHealthCheck>("hangfire", tags: ["ready"])
            .AddCheck<DocumentStoreHealthCheck>("document-store", tags: ["ready"]);

        services.AddScoped<ProcessOutboxJob>();
        services.AddScoped<IngestRecentReleasesJob>();
        services.AddScoped<BackfillReleasesJob>();
        services.AddScoped<SourceHealthCheckJob>();
        services.AddScoped<DownloadTenderDocumentsJob>();
        services.AddScoped<ExtractDocumentTextJob>();
        services.AddScoped<IPipelineScheduler, HangfirePipelineScheduler>();

        return services;
    }
}
