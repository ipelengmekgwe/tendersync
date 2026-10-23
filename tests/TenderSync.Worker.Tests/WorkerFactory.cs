using System.Net.Http.Headers;
using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using TenderSync.Infrastructure;
using TenderSync.Application.Abstractions.Identity;
using TenderSync.Application.Common.Events;
using TenderSync.Application.Security;
using TenderSync.Domain.Events;
using TenderSync.Infrastructure.Identity;
using TenderSync.Infrastructure.Persistence;
using TenderSync.Infrastructure.Persistence.Seed;

namespace TenderSync.Worker.Tests;

/// <summary>
/// Hosts the real Worker against a PostgreSQL 17 container with the Hangfire server disabled, so jobs run only when a test
/// invokes them. Migrates and seeds the database itself (the worker does not migrate on start) and adds a Viewer user.
/// </summary>
public sealed class WorkerFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string AdminEmail = "admin@tendersync.test";
    public const string AdminPassword = "Admin-Password-123!";
    public const string ViewerEmail = "viewer@tendersync.test";
    public const string ViewerPassword = "Viewer-Password-123!";
    public const string SigningKey = "test-signing-key-that-is-at-least-32-characters-long";

    private readonly PostgreSqlContainer _db = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("tendersync")
        .WithUsername("tendersync")
        .WithPassword("tendersync")
        .Build();

    public RecordingHandler Recorded { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:TenderSync"] = _db.GetConnectionString(),
            ["Jwt:SigningKey"] = SigningKey,
            ["Hangfire:ServerEnabled"] = "false",
            ["Seed:AdminEmail"] = AdminEmail,
            ["Seed:AdminPassword"] = AdminPassword,
            ["Serilog:WriteTo:1:Name"] = "Console",
            ["Serilog:MinimumLevel:Default"] = "Warning",
        }));
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton(Recorded);
            services.AddTransient<INotificationHandler<DomainEventNotification<TenderFirstSeen>>>(sp => sp.GetRequiredService<RecordingHandler>());
        });
    }

    public async Task InitializeAsync()
    {
        await _db.StartAsync();

        // The worker's host must not start before the schema exists: migrate with a throwaway provider first.
        await using (var provider = new ServiceCollection()
            .AddLogging()
            .AddInfrastructure(_db.GetConnectionString())
            .Configure<SeedOptions>(o => { o.AdminEmail = AdminEmail; o.AdminPassword = AdminPassword; })
            .BuildServiceProvider())
        {
            using var scope = provider.CreateScope();
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
            await scope.ServiceProvider.GetRequiredService<DatabaseSeeder>().SeedAsync();
        }

        using var hostScope = Services.CreateScope();
        var users = hostScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        if (await users.FindByEmailAsync(ViewerEmail) is null)
        {
            var viewer = new ApplicationUser { UserName = ViewerEmail, Email = ViewerEmail, EmailConfirmed = true, DisplayName = "Test Viewer", CreatedAt = DateTimeOffset.UtcNow };
            (await users.CreateAsync(viewer, ViewerPassword)).Succeeded.Should().BeTrue();
            (await users.AddToRoleAsync(viewer, Roles.Viewer)).Succeeded.Should().BeTrue();
        }
    }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        await _db.DisposeAsync();
    }

    /// <summary>Mints an access token the way the API would, without an HTTP login (the worker has no login endpoint).</summary>
    public async Task<string> AccessTokenForAsync(string email, string password)
    {
        using var scope = Services.CreateScope();
        var identity = scope.ServiceProvider.GetRequiredService<IIdentityService>();
        var tokens = scope.ServiceProvider.GetRequiredService<ITokenService>();
        var user = await identity.AuthenticateAsync(email, password) ?? throw new InvalidOperationException($"Could not authenticate {email}");
        return (await tokens.IssueAsync(user, null)).AccessToken;
    }

    public async Task<HttpClient> CreateClientAsAsync(string email, string password)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await AccessTokenForAsync(email, password));
        return client;
    }
}

/// <summary>Captures TenderFirstSeen events dispatched from the outbox.</summary>
public sealed class RecordingHandler : INotificationHandler<DomainEventNotification<TenderFirstSeen>>
{
    private readonly List<TenderFirstSeen> _events = [];

    public IReadOnlyList<TenderFirstSeen> Events
    {
        get
        {
            lock (_events)
            {
                return [.. _events];
            }
        }
    }

    public Task Handle(DomainEventNotification<TenderFirstSeen> notification, CancellationToken cancellationToken)
    {
        lock (_events)
        {
            _events.Add(notification.Event);
        }

        return Task.CompletedTask;
    }
}

[CollectionDefinition(Name)]
public sealed class WorkerCollection : ICollectionFixture<WorkerFactory>
{
    public const string Name = "worker";
}
