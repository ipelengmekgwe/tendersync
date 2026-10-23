using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace TenderSync.Api.Infrastructure;

/// <summary>Adds the Bearer JWT scheme to the OpenAPI document so Scalar's "Authorize" works.</summary>
internal sealed class BearerSecuritySchemeTransformer : IOpenApiDocumentTransformer
{
    private const string SchemeName = "Bearer";

    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>(StringComparer.Ordinal);
        document.Components.SecuritySchemes[SchemeName] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            In = ParameterLocation.Header,
            Description = "Paste the access token from POST /api/v1/auth/login.",
        };

        document.Security ??= [];
        document.Security.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference(SchemeName, document)] = [],
        });

        document.Info.Title = "TenderSync API";
        document.Info.Description = "Monitors South African eTenders, extracts bid-pack requirements and scores qualification. Data: National Treasury eTenders (PDDL). Verify in the original bid document.";

        return Task.CompletedTask;
    }
}
