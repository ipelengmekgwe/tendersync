using System.Globalization;
using Hangfire;
using Serilog;
using TenderSync.Application;
using TenderSync.Infrastructure;
using TenderSync.Worker;
using TenderSync.Worker.Extensions;
using TenderSync.Worker.Jobs;

Log.Logger = new LoggerConfiguration().WriteTo.Console(formatProvider: CultureInfo.InvariantCulture).CreateBootstrapLogger();

try
{
    var cli = WorkerCli.Parse(args);
    var builder = WebApplication.CreateBuilder(args);

    // Optional per-developer overrides, git-ignored (appsettings.*.local.json). Loaded last, so they win over user-secrets and
    // environment variables; put only the keys you mean to override in it.
    builder.Configuration.AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.local.json", optional: true, reloadOnChange: true);

    if (cli.IsOneShot)
    {
        builder.Configuration["Hangfire:ServerEnabled"] = "false";
    }

    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .Enrich.WithMachineName()
        .Enrich.WithProperty("Application", "TenderSync.Worker"));

    builder.Services.AddApplication();
    builder.Services.AddInfrastructure(builder.Configuration);
    builder.Services.AddWorkerServices(builder.Configuration);

    var app = builder.Build();

    if (cli.IsOneShot)
    {
        try
        {
            await cli.RunAsync(app.Services, app.Lifetime.ApplicationStopping);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Error(ex, "One-shot run failed");
            Environment.ExitCode = 1;
        }

        return;
    }

    app.UseSerilogRequestLogging();
    app.UseExceptionHandler();
    app.UseStatusCodePages();
    app.UseDashboardTokenCookie();
    app.UseAuthentication();
    app.UseAuthorization();

    app.MapWorkerHealthEndpoints();
    app.MapWorkerDashboard();

    using (var scope = app.Services.CreateScope())
    {
        var recurring = scope.ServiceProvider.GetRequiredService<IRecurringJobManager>();
        JobSchedule.Register(recurring);
        if (app.Configuration.GetValue("Hangfire:ServerEnabled", true))
        {
            JobSchedule.TriggerOnStart(recurring, app.Configuration.GetSection("Hangfire:TriggerOnStart").Get<string[]>() ?? [], app.Logger);
        }
    }

    await app.RunAsync();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "TenderSync.Worker terminated unexpectedly");
    throw;
}
finally
{
    await Log.CloseAndFlushAsync();
}

/// <summary>Exposed so WebApplicationFactory in TenderSync.Worker.Tests can host the worker.</summary>
public partial class Program
{
}
