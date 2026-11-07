using MediatR;
using TenderSync.Api.Extensions;
using TenderSync.Application.Features.Auth;
using TenderSync.Application.Features.Auth.Commands.Login;
using TenderSync.Application.Features.Auth.Commands.Logout;
using TenderSync.Application.Features.Auth.Commands.RefreshToken;
using TenderSync.Application.Features.Auth.Queries.GetCurrentUser;

namespace TenderSync.Api.Endpoints;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        // Sign-in attempts get the tight per-client limit; refresh, logout and "me" are routine calls with a looser one.
        var group = app.MapGroup("/api/v1/auth")
            .WithTags("Auth");

        group.MapPost("/login", async (LoginCommand command, ISender sender, CancellationToken ct) =>
                TypedResults.Ok(await sender.Send(command, ct)))
            .WithSummary("Exchange email and password for an access token and a refresh token.")
            .RequireRateLimiting(ApiServiceExtensions.AuthRateLimitPolicy)
            .Produces<AuthResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesValidationProblem();

        group.MapPost("/refresh", async (RefreshTokenCommand command, ISender sender, CancellationToken ct) =>
                TypedResults.Ok(await sender.Send(command, ct)))
            .WithSummary("Rotate a refresh token. The old token is revoked; reusing it revokes the whole family.")
            .RequireRateLimiting(ApiServiceExtensions.RefreshRateLimitPolicy)
            .Produces<AuthResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapPost("/logout", async (LogoutCommand command, ISender sender, CancellationToken ct) =>
            {
                await sender.Send(command, ct);
                return TypedResults.NoContent();
            })
            .WithSummary("Revoke a refresh token.")
            .RequireAuthorization()
            .RequireRateLimiting(ApiServiceExtensions.RefreshRateLimitPolicy)
            .Produces(StatusCodes.Status204NoContent);

        group.MapGet("/me", async (ISender sender, CancellationToken ct) =>
                TypedResults.Ok(await sender.Send(new GetCurrentUserQuery(), ct)))
            .WithSummary("The authenticated user and roles.")
            .RequireAuthorization()
            .RequireRateLimiting(ApiServiceExtensions.RefreshRateLimitPolicy)
            .Produces<UserDto>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        return app;
    }
}
