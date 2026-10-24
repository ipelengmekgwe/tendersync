using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TenderSync.Application.Abstractions.Identity;
using TenderSync.Application.Security;
using TenderSync.Infrastructure.Persistence;

namespace TenderSync.Infrastructure.Identity;

/// <summary>
/// Credential checks with Identity's lockout counters, without SignInManager (no HttpContext dependency). The bookkeeping
/// writes (failed-attempt counters, last login) are best effort: two sign-ins on the same account at the same instant
/// race on the user's concurrency stamp, and the loser must not turn a correct password into a 500.
/// </summary>
internal sealed class IdentityService(UserManager<ApplicationUser> userManager, AppDbContext context, IOptions<DevAuthOptions> devAuth, ILogger<IdentityService> logger) : IIdentityService
{
    public async Task<AuthenticatedUser?> AuthenticateAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        if (devAuth.Value.AllowAnyCredentials)
        {
            return await SignInAnyoneAsync(email);
        }

        var user = await userManager.FindByEmailAsync(email);
        if (user is null || await userManager.IsLockedOutAsync(user))
        {
            return null;
        }

        if (!await userManager.CheckPasswordAsync(user, password))
        {
            await BestEffortAsync(user, () => userManager.AccessFailedAsync(user));
            return null;
        }

        if (user.AccessFailedCount > 0)
        {
            await BestEffortAsync(user, () => userManager.ResetAccessFailedCountAsync(user));
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
        await BestEffortAsync(user, () => userManager.UpdateAsync(user));
    }

    /// <summary>DevAuth:AllowAnyCredentials (Development only): the email signs in regardless of password; an unknown email becomes an Admin with no password set.</summary>
    private async Task<AuthenticatedUser> SignInAnyoneAsync(string email)
    {
        var user = await userManager.FindByEmailAsync(email);
        if (user is null)
        {
            user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                DisplayName = email.Split('@')[0],
                CreatedAt = DateTimeOffset.UtcNow,
            };
            var created = await userManager.CreateAsync(user);
            if (!created.Succeeded)
            {
                throw new InvalidOperationException($"DevAuth could not create '{email}': {string.Join("; ", created.Errors.Select(e => e.Description))}");
            }

            await userManager.AddToRoleAsync(user, Roles.Admin);
            logger.LogWarning("DevAuth bypass created Admin account {Email}; this only happens in Development", email);
        }

        logger.LogWarning("DevAuth bypass signed in {Email} without a password check", email);
        return await ToAuthenticatedUserAsync(user);
    }

    /// <summary>Runs an Identity update; when it loses a concurrency race the tracked user is reset so the request's own SaveChanges does not retry the stale row.</summary>
    private async Task BestEffortAsync(ApplicationUser user, Func<Task<IdentityResult>> update)
    {
        var result = await update();
        if (!result.Succeeded)
        {
            context.Entry(user).State = EntityState.Unchanged;
        }
    }

    private async Task<AuthenticatedUser> ToAuthenticatedUserAsync(ApplicationUser user)
    {
        var roles = await userManager.GetRolesAsync(user);
        return new AuthenticatedUser(user.Id, user.Email ?? string.Empty, user.DisplayName ?? user.Email ?? user.Id.ToString(), [.. roles]);
    }
}
