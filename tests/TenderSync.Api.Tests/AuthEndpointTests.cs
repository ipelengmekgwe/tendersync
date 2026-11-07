using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TenderSync.Application.Features.Auth;
using TenderSync.Application.Security;
using TenderSync.Infrastructure.Identity;
using TenderSync.Infrastructure.Persistence;

namespace TenderSync.Api.Tests;

[Collection(ApiCollection.Name)]
[Trait("Category", "Integration")]
public class AuthEndpointTests(ApiFactory factory)
{
    [Fact]
    public async Task Login_with_valid_credentials_returns_tokens_and_the_admin_role()
    {
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { email = ApiFactory.AdminEmail, password = ApiFactory.AdminPassword });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var auth = await response.Content.ReadFromJsonAsync<AuthResponse>();
        auth!.AccessToken.Should().NotBeNullOrWhiteSpace();
        auth.RefreshToken.Should().NotBeNullOrWhiteSpace();
        auth.AccessTokenExpiresAt.Should().BeAfter(DateTimeOffset.UtcNow);
        auth.User.Email.Should().Be(ApiFactory.AdminEmail);
        auth.User.Roles.Should().Contain(Roles.Admin);
    }

    [Fact]
    public async Task Login_with_a_wrong_password_returns_401_problem_details()
    {
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { email = ApiFactory.AdminEmail, password = "not-the-password" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        problem!.Title.Should().Be("Unauthorized");
        problem.Extensions.Should().ContainKey("traceId");
    }

    [Fact]
    public async Task Login_with_an_invalid_email_returns_400_validation_problem()
    {
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { email = "not-an-email", password = "x" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        problem!.Errors.Should().ContainKey("Email");
    }

    [Fact]
    public async Task Me_requires_a_bearer_token()
    {
        using var anonymous = factory.CreateClient();
        (await anonymous.GetAsync("/api/v1/auth/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        using var viewer = await factory.CreateViewerClientAsync();
        var me = await viewer.GetFromJsonAsync<UserDto>("/api/v1/auth/me");
        me!.Email.Should().Be(ApiFactory.ViewerEmail);
        me.Roles.Should().Equal(Roles.Viewer);
    }

    [Fact]
    public async Task Refresh_rotates_the_token_and_reuse_of_the_old_one_is_rejected()
    {
        using var client = factory.CreateClient();
        var first = await factory.LoginAsync(ApiFactory.ViewerEmail, ApiFactory.ViewerPassword);

        var refreshed = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = first.RefreshToken });
        refreshed.StatusCode.Should().Be(HttpStatusCode.OK);
        var second = (await refreshed.Content.ReadFromJsonAsync<AuthResponse>())!;
        second.RefreshToken.Should().NotBe(first.RefreshToken);

        // Straight after the rotation a second use is two requests racing: refused, but the new token stays valid.
        var race = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = first.RefreshToken });
        race.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var third = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = second.RefreshToken });
        third.StatusCode.Should().Be(HttpStatusCode.OK, "a concurrent refresh must not sign the user out");
        var current = (await third.Content.ReadFromJsonAsync<AuthResponse>())!;

        // Later, reuse means the token leaked: it is rejected and the whole family is revoked.
        using (var scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await context.Set<RefreshToken>()
                .Where(t => t.RevokedAt != null)
                .ExecuteUpdateAsync(set => set.SetProperty(t => t.RevokedAt, t => t.RevokedAt!.Value.AddMinutes(-5)));
        }

        var reuse = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = first.RefreshToken });
        reuse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var afterReuse = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = current.RefreshToken });
        afterReuse.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "reuse revoked the whole family");
    }

    [Fact]
    public async Task Logout_revokes_the_refresh_token()
    {
        var auth = await factory.LoginAsync(ApiFactory.ViewerEmail, ApiFactory.ViewerPassword);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);

        var logout = await client.PostAsJsonAsync("/api/v1/auth/logout", new { refreshToken = auth.RefreshToken });
        logout.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var refresh = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = auth.RefreshToken });
        refresh.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_tampered_access_token_is_rejected()
    {
        var auth = await factory.LoginAsync(ApiFactory.ViewerEmail, ApiFactory.ViewerPassword);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken[..^4] + "AAAA");

        (await client.GetAsync("/api/v1/auth/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
