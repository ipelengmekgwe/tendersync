using Microsoft.AspNetCore.Identity;
using TenderSync.Application.Abstractions.Identity;

namespace TenderSync.Infrastructure.Identity;

/// <summary>Credential checks with Identity's lockout counters, without SignInManager (no HttpContext dependency).</summary>
internal sealed class IdentityService(UserManager<ApplicationUser> userManager) : IIdentityService
{
    public async Task<AuthenticatedUser?> AuthenticateAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByEmailAsync(email);
        if (user is null || await userManager.IsLockedOutAsync(user))
        {
            return null;
        }

        if (!await userManager.CheckPasswordAsync(user, password))
        {
            await userManager.AccessFailedAsync(user);
            return null;
        }

        if (user.AccessFailedCount > 0)
        {
            await userManager.ResetAccessFailedCountAsync(user);
        }

        return await ToAuthenticatedUserAsync(user);
    }

    public async Task<AuthenticatedUser?> GetUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        return user is null ? null : await ToAuthenticatedUserAsync(user);
    }

    public async Task RecordLoginAsync(Guid userId, DateTimeOffset at, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return;
        }

        user.LastLoginAt = at;
        await userManager.UpdateAsync(user);
    }

    private async Task<AuthenticatedUser> ToAuthenticatedUserAsync(ApplicationUser user)
    {
        var roles = await userManager.GetRolesAsync(user);
        return new AuthenticatedUser(user.Id, user.Email ?? string.Empty, user.DisplayName ?? user.Email ?? user.Id.ToString(), [.. roles]);
    }
}
