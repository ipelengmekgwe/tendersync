using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TenderSync.Application.Abstractions;
using TenderSync.Application.Security;
using TenderSync.Domain.Enums;
using TenderSync.Domain.Geography;
using TenderSync.Infrastructure.Identity;
using TenderSync.Infrastructure.Persistence;
using TenderSync.Infrastructure.Persistence.Seed;

namespace TenderSync.Infrastructure.Tests.Persistence;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public class SeedTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Seeds_the_fourteen_interest_categories()
    {
        using var scope = fixture.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var categories = await context.InterestCategorySet.AsNoTracking().ToListAsync();

        categories.Should().HaveCount(14);
        categories.Select(c => c.Code).Should().Contain(["SW_DEV", "WEB_DEV", "CYBER", "DB_DEV"]);
        categories.Single(c => c.Code == "APP_SUPPORT").NegativeKeywords.Should().Contain("irrigation system");
    }

    [Fact]
    public async Task Seeds_all_south_african_municipalities()
    {
        using var scope = fixture.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var municipalities = await context.MunicipalitySet.AsNoTracking().ToListAsync();

        municipalities.Should().HaveCount(257, "8 metros + 44 districts + 205 locals");
        municipalities.Count(m => m.Type == MunicipalityType.Metropolitan).Should().Be(8);
        municipalities.Count(m => m.Type == MunicipalityType.District).Should().Be(44);
        municipalities.Single(m => m.Code == "LIM333").Should().Match<Municipality>(m => m.Province == Province.Limpopo && m.NormalisedName == "greater tzaneen");
    }

    [Fact]
    public async Task Seeding_twice_is_idempotent()
    {
        using var scope = fixture.CreateScope();
        var seeder = scope.ServiceProvider.GetRequiredService<DatabaseSeeder>();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await seeder.SeedAsync();

        (await context.InterestCategorySet.CountAsync()).Should().Be(14);
        (await context.MunicipalitySet.CountAsync()).Should().Be(257);
    }

    [Fact]
    public async Task Seeds_roles_and_the_configured_admin()
    {
        using var scope = fixture.CreateScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        foreach (var role in Roles.All)
        {
            (await roleManager.RoleExistsAsync(role)).Should().BeTrue(role);
        }

        var admin = await userManager.FindByEmailAsync(PostgresFixture.AdminEmail);
        admin.Should().NotBeNull();
        (await userManager.IsInRoleAsync(admin!, Roles.Admin)).Should().BeTrue();
        (await userManager.CheckPasswordAsync(admin!, PostgresFixture.AdminPassword)).Should().BeTrue();
    }

    [Fact]
    public void Municipality_lookup_resolves_normalised_names_and_unique_prefixes()
    {
        var lookup = fixture.Services.GetRequiredService<IMunicipalityLookup>();

        lookup.TryResolve("greater tzaneen").Should().Be(Province.Limpopo);
        lookup.TryResolve("cape town").Should().Be(Province.WesternCape);
        lookup.TryResolve("ingquza hill").Should().Be(Province.EasternCape);
        lookup.TryResolve("emalahleni").Should().BeNull("Emalahleni exists in two provinces");
        lookup.TryResolve("narnia").Should().BeNull();
    }
}
