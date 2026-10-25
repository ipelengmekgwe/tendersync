using System.Globalization;
using Scalar.AspNetCore;
using Serilog;
using TenderSync.Api.Endpoints;
using TenderSync.Api.Extensions;
using TenderSync.Application;
using TenderSync.Infrastructure;
using TenderSync.Infrastructure.Identity;

Log.Logger = new LoggerConfiguration().WriteTo.Console(formatProvider: CultureInfo.InvariantCulture).CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // Optional per-developer overrides, git-ignored (appsettings.*.local.json). Loaded last, so they win over user-secrets and
    // environment variables; put only the keys you mean to override in it.
    builder.Configuration.AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.local.json", optional: true, reloadOnChange: true);

    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .Enrich.WithMachineName()
        .Enrich.WithProperty("Application", "TenderSync.Api"));

    builder.Services.AddApplication();
    builder.Services.AddInfrastructure(builder.Configuration);
    builder.Services.AddApiServices(builder.Configuration);

    // DevAuth:AllowAnyCredentials is a local testing shortcut and must never survive outside Development, whatever the config says.
    var isDevelopment = builder.Environment.IsDevelopment();
    builder.Services.PostConfigure<DevAuthOptions>(options => options.AllowAnyCredentials = options.AllowAnyCredentials && isDevelopment);

    var app = builder.Build();

    if (app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<DevAuthOptions>>().Value.AllowAnyCredentials)
    {
        Log.Warning("DevAuth:AllowAnyCredentials is on: any email and password sign in as Admin. Development only; set DevAuth:AllowAnyCredentials=false to restore real sign-in");
    }

    app.UseSerilogRequestLogging();
    app.UseExceptionHandler();
    app.UseStatusCodePages();
    app.UseForwardedHeaders();

    if (!app.Environment.IsDevelopment())
    {
        app.UseHsts();
    }

    app.UseCors();
    app.UseRateLimiter();
    app.UseAuthentication();
    app.UseAuthorization();

    app.MapOpenApi();
    app.MapScalarApiReference(options => options.WithTitle("TenderSync API").WithTheme(ScalarTheme.Purple));
    app.MapHealthEndpoints();
    app.MapAuthEndpoints();
    app.MapTenderEndpoints();
    app.MapDocumentEndpoints();
    app.MapMatchEndpoints();
    app.MapInterestCategoryEndpoints();
    app.MapCompanyProfileEndpoints();
    app.MapNotificationEndpoints();
    app.MapAdminEndpoints();

    await app.MigrateAndSeedAsync();
    await app.RunAsync();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "TenderSync.Api terminated unexpectedly");
    throw;
}
finally
{
    await Log.CloseAndFlushAsync();
}

/// <summary>Exposed so WebApplicationFactory in TenderSync.Api.Tests can host the app.</summary>
public partial class Program
{
}
