using System.Security.Claims;
using Microsoft.IdentityModel.JsonWebTokens;
using TenderSync.Application.Abstractions;
using TenderSync.Infrastructure.Identity;

namespace TenderSync.Api.Infrastructure;

/// <summary>The caller as seen from the validated JWT on the current request.</summary>
internal sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    public Guid? UserId => Guid.TryParse(Principal?.FindFirstValue(JwtRegisteredClaimNames.Sub), out var id) ? id : null;

    public string? Email => Principal?.FindFirstValue(JwtClaims.Email);

    public string DisplayName => Principal?.FindFirstValue(JwtClaims.Name) ?? Email ?? "anonymous";

    public IReadOnlyCollection<string> Roles => Principal?.FindAll(JwtClaims.Role).Select(c => c.Value).ToArray() ?? [];

    public string? IpAddress => accessor.HttpContext?.Connection.RemoteIpAddress?.ToString();
}
