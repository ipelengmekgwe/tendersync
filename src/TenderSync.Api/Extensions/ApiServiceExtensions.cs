using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using TenderSync.Api.Infrastructure;
using TenderSync.Api.Security;
using TenderSync.Application.Abstractions;
using TenderSync.Application.Abstractions.Jobs;
using TenderSync.Application.Jobs;
using TenderSync.Application.Security;
using TenderSync.Infrastructure.Identity;
using TenderSync.Infrastructure.Persistence;
using TenderSync.Infrastructure.Storage;

namespace TenderSync.Api.Extensions;

public static class ApiServiceExtensions
{
    public const string AuthRateLimitPolicy = "auth";
    public const string RefreshRateLimitPolicy = "auth-refresh";

    /// <summary>Private and loopback ranges: the dashboard container, the Compose network, the Container Apps ingress.</summary>
    private static readonly string[] TrustedProxyNetworks = ["10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16", "100.64.0.0/10", "127.0.0.0/8", "::1/128", "fc00::/7"];

    /// <summary>
    /// Cross-cutting API services: current user, errors, JWT auth, role policies, rate limiting, CORS, OpenAPI, health.
    /// Configuration is read inside the options lambdas (lazily) so test hosts can override it after registration.
    /// </summary>
    public static IServiceCollection AddApiServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, HttpCurrentUser>();

        services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
        {
            context.ProblemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
        });
        services.AddExceptionHandler<GlobalExceptionHandler>();

        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

            // Every call reaches the API through the dashboard's server, and on Azure through the Container Apps ingress
            // first; both sit on private addresses. Trust X-Forwarded-For hops only from private networks and walk the
            // chain to the first public address, so rate limits and audit rows see the browser rather than the dashboard
            // container. A hop a client wrote itself sits left of the real address and is never reached.
            options.ForwardLimit = null;
            options.KnownProxies.Clear();
            options.KnownIPNetworks.Clear();
            foreach (var network in TrustedProxyNetworks)
            {
                options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
            }
        });

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
            });

        services.AddAuthorizationBuilder()
            .AddPolicy(Policies.Viewer, policy => policy.RequireRole(RoleHierarchy.RolesAtOrAbove(Roles.Viewer)))
            .AddPolicy(Policies.Reviewer, policy => policy.RequireRole(RoleHierarchy.RolesAtOrAbove(Roles.Reviewer)))
            .AddPolicy(Policies.Admin, policy => policy.RequireRole(RoleHierarchy.RolesAtOrAbove(Roles.Admin)));

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            AddPerClientPolicy(options, AuthRateLimitPolicy, configuration.GetSection("RateLimiting:Auth"), defaultPermitLimit: 10);

            // Refreshes are routine (every access token lives 15 minutes and every open tab refreshes), so they get their
            // own, looser bucket instead of using up the sign-in attempts.
            AddPerClientPolicy(options, RefreshRateLimitPolicy, configuration.GetSection("RateLimiting:Refresh"), defaultPermitLimit: 60);
        });

        services.AddCors(options => options.AddDefaultPolicy(policy =>
        {
            var origins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
            if (origins.Length > 0)
            {
                policy.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod().AllowCredentials();
            }
        }));

        services.AddOpenApi("v1", options =>
        {
            options.AddDocumentTransformer<BearerSecuritySchemeTransformer>();
            // The JSON options accept numbers written as strings, which the generator describes as "integer | string" with a
            // pattern. The generated TypeScript client (web/) is far more useful with plain integers.
            options.AddSchemaTransformer((schema, _, _) =>
            {
                if (schema.Type is { } type && type.HasFlag(Microsoft.OpenApi.JsonSchemaType.String) && (type.HasFlag(Microsoft.OpenApi.JsonSchemaType.Integer) || type.HasFlag(Microsoft.OpenApi.JsonSchemaType.Number)))
                {
                    schema.Type = type & ~Microsoft.OpenApi.JsonSchemaType.String;
                    schema.Pattern = null;
                }

                return Task.CompletedTask;
            });
        });

        services.AddHealthChecks()
            .AddDbContextCheck<AppDbContext>("database", tags: ["ready"])
            .AddCheck<DocumentStoreHealthCheck>("document-store", tags: ["ready"]);

        // The API never dispatches outbox events, so the event handlers that enqueue pipeline jobs are never invoked here.
        services.AddSingleton<IPipelineScheduler, NoPipelineScheduler>();

        return services;
    }

    /// <summary>A fixed window per client address (after forwarded headers), sized from {section}:PermitLimit and WindowSeconds.</summary>
    private static void AddPerClientPolicy(RateLimiterOptions options, string policy, IConfiguration section, int defaultPermitLimit)
    {
        var permitLimit = section.GetValue("PermitLimit", defaultPermitLimit);
        var windowSeconds = section.GetValue("WindowSeconds", 60);
        options.AddPolicy(policy, context => RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permitLimit,
                Window = TimeSpan.FromSeconds(windowSeconds),
                QueueLimit = 0,
            }));
    }
}
