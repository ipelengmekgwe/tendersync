using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using TenderSync.Application.Features.Auth;
using TenderSync.Application.Security;
using TenderSync.Infrastructure.Identity;

namespace TenderSync.Api.Tests;

/// <summary>
/// Hosts the real API against a PostgreSQL 17 container. The app migrates and seeds on start (admin from config);
/// a Viewer user is added for role tests. Needs Docker.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
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

    public string DocumentRoot { get; } = Path.Combine(Path.GetTempPath(), "tendersync-api-tests", Guid.NewGuid().ToString("N"));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:TenderSync"] = _db.GetConnectionString(),
            ["Jwt:SigningKey"] = SigningKey,
            ["Jwt:AccessTokenMinutes"] = "5",
            ["Database:MigrateOnStartup"] = "true",
            ["Database:SeedOnStartup"] = "true",
            ["Seed:AdminEmail"] = AdminEmail,
            ["Seed:AdminPassword"] = AdminPassword,
            ["RateLimiting:Auth:PermitLimit"] = "1000",
            ["Serilog:WriteTo:1:Name"] = "Console",
            ["Serilog:MinimumLevel:Default"] = "Warning",
            ["Llm:Routing:classify"] = "fake",
            ["Llm:Routing:extract-requirements"] = "fake",
            ["Llm:Routing:estimate-functionality"] = "fake",
            ["Llm:Routing:fuzzy-match-certification"] = "fake",
            ["Encryption:Keys:test"] = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)),
            ["Encryption:ActiveKeyId"] = "test",
            ["DocumentStore:Provider"] = "FileSystem",
            ["DocumentStore:FileSystem:RootPath"] = DocumentRoot,
        }));
    }

    public async Task InitializeAsync()
    {
        await _db.StartAsync();

        // Touching Services builds and starts the host (migrations + seed run in Program before RunAsync).
        using var scope = Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
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
        if (Directory.Exists(DocumentRoot))
        {
            Directory.Delete(DocumentRoot, recursive: true);
        }
    }

    public async Task<AuthResponse> LoginAsync(string email, string password)
    {
        using var client = CreateClient();
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthResponse>())!;
    }

    public async Task<HttpClient> CreateClientAsAsync(string email, string password)
    {
        var auth = await LoginAsync(email, password);
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        return client;
    }

    public Task<HttpClient> CreateAdminClientAsync() => CreateClientAsAsync(AdminEmail, AdminPassword);

    public Task<HttpClient> CreateViewerClientAsync() => CreateClientAsAsync(ViewerEmail, ViewerPassword);
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFactory>
{
    public const string Name = "api";
}
