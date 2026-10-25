using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TenderSync.Application.Abstractions.Identity;
using TenderSync.Application.Common.Exceptions;
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
    private async Task<AuthenticatedUser?> SignInAnyoneAsync(string email)
    {
        var user = await userManager.FindByEmailAsync(email);
        if (user is not null && IsDisabled(user))
        {
            return null;
        }

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

    public async Task<IReadOnlyList<UserAccount>> ListUsersAsync(CancellationToken cancellationToken = default)
    {
        var users = await userManager.Users.AsNoTracking().OrderBy(u => u.Email).ToListAsync(cancellationToken);
        var accounts = new List<UserAccount>(users.Count);
        foreach (var user in users)
        {
            accounts.Add(await ToAccountAsync(user));
        }

        return accounts;
    }

    public async Task<UserAccount> CreateUserAsync(string email, string displayName, string role, string password, CancellationToken cancellationToken = default)
    {
        if (await userManager.FindByEmailAsync(email) is not null)
        {
            throw new ConflictException($"An account for {email} already exists.");
        }

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            DisplayName = displayName,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        Ensure(await userManager.CreateAsync(user, password), nameof(password));
        Ensure(await userManager.AddToRoleAsync(user, role), nameof(role));
        return await ToAccountAsync(user);
    }

    public async Task<UserAccount?> UpdateUserAsync(Guid userId, string displayName, string role, bool disabled, string? newPassword, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return null;
        }

        if (!string.IsNullOrEmpty(newPassword))
        {
            // Validate before touching anything, so a weak password leaves the account as it was.
            foreach (var validator in userManager.PasswordValidators)
            {
                Ensure(await validator.ValidateAsync(userManager, user, newPassword), nameof(newPassword));
            }
        }

        user.DisplayName = displayName;
        Ensure(await userManager.UpdateAsync(user), nameof(displayName));

        var roles = await userManager.GetRolesAsync(user);
        if (roles.Count != 1 || !string.Equals(roles[0], role, StringComparison.Ordinal))
        {
            Ensure(await userManager.RemoveFromRolesAsync(user, roles), nameof(role));
            Ensure(await userManager.AddToRoleAsync(user, role), nameof(role));
        }

        var wasDisabled = IsDisabled(user);
        if (disabled != wasDisabled)
        {
            Ensure(await userManager.SetLockoutEndDateAsync(user, disabled ? DateTimeOffset.MaxValue : null), nameof(disabled));
            Ensure(await userManager.UpdateSecurityStampAsync(user), nameof(disabled));
        }

        if (disabled)
        {
            var now = DateTimeOffset.UtcNow;
            foreach (var token in await context.Set<RefreshToken>().Where(t => t.UserId == user.Id && t.RevokedAt == null).ToListAsync(cancellationToken))
            {
                token.Revoke(now, null);
            }
        }

        if (!string.IsNullOrEmpty(newPassword))
        {
            // No token providers are registered (AddIdentityCore), so replace the hash directly; validators already ran above.
            if (await userManager.HasPasswordAsync(user))
            {
                Ensure(await userManager.RemovePasswordAsync(user), nameof(newPassword));
            }

            Ensure(await userManager.AddPasswordAsync(user, newPassword), nameof(newPassword));
            Ensure(await userManager.ResetAccessFailedCountAsync(user), nameof(newPassword));
        }

        return await ToAccountAsync(user);
    }

    /// <summary>Disabled is an indefinite lockout (Identity's own way to switch an account off).</summary>
    private static bool IsDisabled(ApplicationUser user) => user.LockoutEnd is { } end && end.UtcDateTime.Year >= 9999;

    private static void Ensure(IdentityResult result, string property)
    {
        if (!result.Succeeded)
        {
            throw new ValidationException(result.Errors.Select(e => new ValidationFailure(property, e.Description)));
        }
    }

    private async Task<UserAccount> ToAccountAsync(ApplicationUser user)
    {
        var roles = await userManager.GetRolesAsync(user);
        var disabled = IsDisabled(user);
        var lockedUntil = !disabled && user.LockoutEnd > DateTimeOffset.UtcNow ? user.LockoutEnd : null;
        return new UserAccount(user.Id, user.Email ?? string.Empty, user.DisplayName ?? user.Email ?? string.Empty, [.. roles.Order(StringComparer.Ordinal)], disabled, lockedUntil, user.CreatedAt, user.LastLoginAt);
    }

    private async Task<AuthenticatedUser> ToAuthenticatedUserAsync(ApplicationUser user)
    {
        var roles = await userManager.GetRolesAsync(user);
        return new AuthenticatedUser(user.Id, user.Email ?? string.Empty, user.DisplayName ?? user.Email ?? user.Id.ToString(), [.. roles]);
    }
}
