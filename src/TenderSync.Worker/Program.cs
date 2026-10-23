using System.Globalization;
using Hangfire;
using Serilog;
using TenderSync.Application;
using TenderSync.Infrastructure;
using TenderSync.Worker.Extensions;
using TenderSync.Worker.Jobs;

Log.Logger = new LoggerConfiguration().WriteTo.Console(formatProvider: CultureInfo.InvariantCulture).CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

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
        JobSchedule.Register(scope.ServiceProvider.GetRequiredService<IRecurringJobManager>());
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
