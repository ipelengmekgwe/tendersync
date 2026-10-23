using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using TenderSync.Api.Infrastructure;
using TenderSync.Api.Security;
using TenderSync.Application.Abstractions;
using TenderSync.Application.Security;
using TenderSync.Infrastructure.Identity;
using TenderSync.Infrastructure.Persistence;

namespace TenderSync.Api.Extensions;

public static class ApiServiceExtensions
{
    public const string AuthRateLimitPolicy = "auth";

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
            var permitLimit = configuration.GetValue("RateLimiting:Auth:PermitLimit", 10);
            var windowSeconds = configuration.GetValue("RateLimiting:Auth:WindowSeconds", 60);

            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(AuthRateLimitPolicy, context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = permitLimit,
                    Window = TimeSpan.FromSeconds(windowSeconds),
                    QueueLimit = 0,
                }));
        });

        services.AddCors(options => options.AddDefaultPolicy(policy =>
        {
            var origins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
            if (origins.Length > 0)
            {
                policy.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod().AllowCredentials();
            }
        }));

        services.AddOpenApi("v1", options => options.AddDocumentTransformer<BearerSecuritySchemeTransformer>());

        services.AddHealthChecks()
            .AddDbContextCheck<AppDbContext>("database", tags: ["ready"]);

        return services;
    }
}
