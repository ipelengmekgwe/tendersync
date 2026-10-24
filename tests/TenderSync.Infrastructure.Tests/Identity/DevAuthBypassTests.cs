using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TenderSync.Application.Abstractions.Identity;
using TenderSync.Application.Security;
using TenderSync.Infrastructure;
using TenderSync.Infrastructure.Identity;

namespace TenderSync.Infrastructure.Tests.Identity;

/// <summary>DevAuth:AllowAnyCredentials provisions an Admin for any email; off, unknown users and wrong passwords stay 401.</summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public class DevAuthBypassTests(PostgresFixture fixture)
{
    private ServiceProvider Build(bool allowAny) => new ServiceCollection()
        .AddLogging(l => l.SetMinimumLevel(LogLevel.Warning))
        .AddInfrastructure(fixture.ConnectionString)
        .Configure<DevAuthOptions>(o => o.AllowAnyCredentials = allowAny)
        .BuildServiceProvider();

    [Fact]
    public async Task With_the_bypass_on_any_email_signs_in_and_is_created_as_admin_once()
    {
        var email = $"anyone-{Guid.NewGuid():N}@example.test";
        await using var provider = Build(allowAny: true);
        using var scope = provider.CreateScope();
        var identity = scope.ServiceProvider.GetRequiredService<IIdentityService>();

        var first = await identity.AuthenticateAsync(email, "whatever");
        var second = await identity.AuthenticateAsync(email, "something else entirely");

        first.Should().NotBeNull();
        first!.Email.Should().Be(email);
        first.Roles.Should().Contain(Roles.Admin);
        first.DisplayName.Should().Be(email.Split('@')[0]);
        second!.Id.Should().Be(first.Id, "the second sign-in reuses the account");
    }

    [Fact]
    public async Task With_the_bypass_off_unknown_users_and_wrong_passwords_are_rejected()
    {
        await using var provider = Build(allowAny: false);
        using var scope = provider.CreateScope();
        var identity = scope.ServiceProvider.GetRequiredService<IIdentityService>();

        (await identity.AuthenticateAsync($"nobody-{Guid.NewGuid():N}@example.test", "whatever")).Should().BeNull();
        (await identity.AuthenticateAsync(PostgresFixture.AdminEmail, "wrong-password")).Should().BeNull();
        (await identity.AuthenticateAsync(PostgresFixture.AdminEmail, PostgresFixture.AdminPassword)).Should().NotBeNull();
    }
}
