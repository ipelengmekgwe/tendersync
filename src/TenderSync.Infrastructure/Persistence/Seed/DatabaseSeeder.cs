using System.Globalization;
using System.Reflection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TenderSync.Application.Abstractions;
using TenderSync.Application.Security;
using TenderSync.Domain.Categories;
using TenderSync.Domain.Enums;
using TenderSync.Domain.Geography;
using TenderSync.Infrastructure.Identity;

namespace TenderSync.Infrastructure.Persistence.Seed;

/// <summary>Idempotent reference-data seed: interest categories (Appendix B), municipalities (Appendix C), roles and the first admin.</summary>
public sealed class DatabaseSeeder(
    AppDbContext context,
    RoleManager<ApplicationRole> roleManager,
    UserManager<ApplicationUser> userManager,
    IOptions<SeedOptions> options,
    IDateTimeProvider clock,
    ILogger<DatabaseSeeder> logger)
{
    public const string MunicipalityResource = "TenderSync.Infrastructure.Persistence.Seed.za-municipalities.csv";

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        await SeedInterestCategoriesAsync(cancellationToken);
        await SeedMunicipalitiesAsync(cancellationToken);
        await SeedRolesAsync();
        await SeedAdminAsync();
    }

    private async Task SeedInterestCategoriesAsync(CancellationToken cancellationToken)
    {
        var existing = await context.InterestCategorySet.Select(c => c.Code).ToListAsync(cancellationToken);
        var missing = InterestCategorySeed.All.Where(c => !existing.Contains(c.Code, StringComparer.OrdinalIgnoreCase)).ToList();
        if (missing.Count == 0)
        {
            return;
        }

        context.InterestCategorySet.AddRange(missing.Select(c => InterestCategory.Create(c.Code, c.Name, c.Description, c.Keywords, c.NegativeKeywords)));
        await context.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Seeded {Count} interest categories", missing.Count);
    }

    private async Task SeedMunicipalitiesAsync(CancellationToken cancellationToken)
    {
        if (await context.MunicipalitySet.AnyAsync(cancellationToken))
        {
            return;
        }

        var rows = ReadMunicipalityCsv().ToList();
        context.MunicipalitySet.AddRange(rows);
        await context.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Seeded {Count} municipalities", rows.Count);
    }

    private async Task SeedRolesAsync()
    {
        foreach (var role in Roles.All)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                var result = await roleManager.CreateAsync(new ApplicationRole(role));
                EnsureSucceeded(result, $"create role {role}");
            }
        }
    }

    private async Task SeedAdminAsync()
    {
        var seed = options.Value;
        if (string.IsNullOrWhiteSpace(seed.AdminEmail) || string.IsNullOrWhiteSpace(seed.AdminPassword))
        {
            if (!await userManager.Users.AnyAsync())
            {
                logger.LogWarning("No users exist and Seed:AdminEmail / Seed:AdminPassword are not configured; no admin was created");
            }

            return;
        }

        if (await userManager.FindByEmailAsync(seed.AdminEmail) is not null)
        {
            return;
        }

        var user = new ApplicationUser
        {
            UserName = seed.AdminEmail,
            Email = seed.AdminEmail,
            EmailConfirmed = true,
            DisplayName = seed.AdminDisplayName ?? "Administrator",
            CreatedAt = clock.UtcNow,
        };

        EnsureSucceeded(await userManager.CreateAsync(user, seed.AdminPassword), "create admin user");
        EnsureSucceeded(await userManager.AddToRoleAsync(user, Roles.Admin), "assign admin role");
        logger.LogInformation("Seeded admin user {Email}", seed.AdminEmail);
    }

    private static void EnsureSucceeded(IdentityResult result, string action)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException($"Seeding failed to {action}: {string.Join("; ", result.Errors.Select(e => e.Description))}");
        }
    }

    public static IEnumerable<Municipality> ReadMunicipalityCsv()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(MunicipalityResource)
            ?? throw new InvalidOperationException($"Embedded resource {MunicipalityResource} not found.");
        using var reader = new StreamReader(stream);

        var header = reader.ReadLine();
        if (header is null || !header.StartsWith("code,name,type,province,district", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Municipality CSV has an unexpected header.");
        }

        while (reader.ReadLine() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var parts = line.Split(',', 5);
            if (parts.Length < 4)
            {
                throw new InvalidOperationException($"Malformed municipality row: {line}");
            }

            var type = Enum.Parse<MunicipalityType>(parts[2], ignoreCase: true);
            var province = ProvinceInfo.Parse(parts[3]);
            if (province == Province.Unknown)
            {
                throw new InvalidOperationException($"Unknown province in municipality row: {line}");
            }

            var district = parts.Length > 4 && parts[4].Length > 0 ? parts[4] : null;
            yield return Municipality.Create(parts[0].ToUpper(CultureInfo.InvariantCulture), parts[1], type, province, district);
        }
    }
}

public sealed class SeedOptions
{
    public const string SectionName = "Seed";

    public string? AdminEmail { get; set; }

    public string? AdminPassword { get; set; }

    public string? AdminDisplayName { get; set; }
}
