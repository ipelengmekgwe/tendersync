using MediatR;
using Microsoft.AspNetCore.Mvc;
using TenderSync.Api.Security;
using TenderSync.Application.Features.Companies;
using TenderSync.Application.Features.Companies.Commands.CreateCompanyProfile;
using TenderSync.Application.Features.Companies.Commands.DeleteCompanyDocument;
using TenderSync.Application.Features.Companies.Commands.DownloadCompanyDocument;
using TenderSync.Application.Features.Companies.Commands.SetCompanyProfileActive;
using TenderSync.Application.Features.Companies.Commands.UpdateCompanyDocument;
using TenderSync.Application.Features.Companies.Commands.UpdateCompanyProfile;
using TenderSync.Application.Features.Companies.Commands.UploadCompanyDocument;
using TenderSync.Application.Features.Companies.Queries.GetCompanyProfile;
using TenderSync.Application.Features.Companies.Queries.ListCompanyDocuments;
using TenderSync.Application.Features.Companies.Queries.ListCompanyProfiles;
using TenderSync.Domain.Enums;

namespace TenderSync.Api.Endpoints;

/// <summary>Company profiles and their encrypted document library (PLAN.md §7.5, §12; WP09).</summary>
public static class CompanyProfileEndpoints
{
    /// <summary>Multipart fields of POST /company-profiles/{id}/documents.</summary>
    public sealed class UploadRequest
    {
        public IFormFile File { get; init; } = null!;

        [FromForm]
        public CompanyDocumentType Type { get; init; }

        /// <summary>Defaults to the file name.</summary>
        [FromForm]
        public string? Title { get; init; }

        [FromForm]
        public DateTimeOffset? ExpiresAt { get; init; }
    }

    public sealed record UpdateDocumentRequest(string Title, DateTimeOffset? ExpiresAt);

    public static IEndpointRouteBuilder MapCompanyProfileEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/company-profiles")
            .WithTags("Company profiles")
            .RequireAuthorization(Policies.Viewer);

        group.MapGet("/", async (bool? includeInactive, ISender sender, CancellationToken ct) =>
                TypedResults.Ok(await sender.Send(new ListCompanyProfilesQuery(includeInactive ?? false), ct)))
            .WithSummary("Company profiles (active ones unless includeInactive=true).")
            .Produces<IReadOnlyList<CompanyProfileListItemDto>>();

        group.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
                TypedResults.Ok(await sender.Send(new GetCompanyProfileQuery(id), ct)))
            .WithSummary("A profile with its compliance data (tax PIN masked), capabilities and document count.")
            .Produces<CompanyProfileDto>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/", async (CompanyProfileInput input, ISender sender, CancellationToken ct) =>
            {
                var created = await sender.Send(new CreateCompanyProfileCommand(input), ct);
                return TypedResults.Created($"/api/v1/company-profiles/{created.Id}", created);
            })
            .RequireAuthorization(Policies.Admin)
            .WithSummary("Creates a profile. Admin.")
            .Produces<CompanyProfileDto>(StatusCodes.Status201Created)
            .ProducesValidationProblem();

        group.MapPut("/{id:guid}", async (Guid id, CompanyProfileInput input, ISender sender, CancellationToken ct) =>
                TypedResults.Ok(await sender.Send(new UpdateCompanyProfileCommand(id, input), ct)))
            .RequireAuthorization(Policies.Admin)
            .WithSummary("Replaces every editable section of the profile. Admin.")
            .Produces<CompanyProfileDto>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapDelete("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
                TypedResults.Ok(await sender.Send(new SetCompanyProfileActiveCommand(id, false), ct)))
            .RequireAuthorization(Policies.Admin)
            .WithSummary("Deactivates a profile (matches and reports keep referencing it). Admin.")
            .Produces<CompanyProfileDto>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/activate", async (Guid id, ISender sender, CancellationToken ct) =>
                TypedResults.Ok(await sender.Send(new SetCompanyProfileActiveCommand(id, true), ct)))
            .RequireAuthorization(Policies.Admin)
            .WithSummary("Reactivates a profile. Admin.")
            .Produces<CompanyProfileDto>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        var documents = group.MapGroup("/{id:guid}/documents");

        documents.MapGet("/", async (Guid id, bool? includeDeleted, ISender sender, CancellationToken ct) =>
                TypedResults.Ok(await sender.Send(new ListCompanyDocumentsQuery(id, includeDeleted ?? false), ct)))
            .WithSummary("The company's document library with expiry flags and the master key each entry is wrapped with.")
            .Produces<IReadOnlyList<CompanyDocumentDto>>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        documents.MapPost("/", async (Guid id, [AsParameters] UploadRequest request, ISender sender, CancellationToken ct) =>
            {
                if (request.File is null)
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["file"] = ["A file is required."] });
                }

                await using var content = request.File.OpenReadStream();
                var created = await sender.Send(new UploadCompanyDocumentCommand(
                    id, request.Type, string.IsNullOrWhiteSpace(request.Title) ? request.File.FileName : request.Title,
                    request.File.FileName, request.File.ContentType, request.File.Length, request.ExpiresAt, content), ct);
                return Results.Created($"/api/v1/company-profiles/{id}/documents/{created.Id}", created);
            })
            .RequireAuthorization(Policies.Reviewer)
            .DisableAntiforgery()
            .WithSummary("Uploads a document (multipart: file, type, title, expiresAt). Encrypted with a per-file key before it reaches the store. Reviewer or Admin.")
            .Accepts<UploadRequest>("multipart/form-data")
            .Produces<CompanyDocumentDto>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        documents.MapGet("/{docId:guid}/download", async (Guid id, Guid docId, ISender sender, CancellationToken ct) =>
            {
                var file = await sender.Send(new DownloadCompanyDocumentCommand(id, docId), ct);
                return Results.Stream(file.Content, file.ContentType, file.FileName);
            })
            .RequireAuthorization(Policies.Reviewer)
            .WithSummary("Streams the decrypted document. Every download is written to the audit log. Reviewer or Admin.")
            .Produces(StatusCodes.Status200OK, contentType: "application/octet-stream")
            .ProducesProblem(StatusCodes.Status404NotFound);

        documents.MapPut("/{docId:guid}", async (Guid id, Guid docId, UpdateDocumentRequest request, ISender sender, CancellationToken ct) =>
                TypedResults.Ok(await sender.Send(new UpdateCompanyDocumentCommand(id, docId, request.Title, request.ExpiresAt), ct)))
            .RequireAuthorization(Policies.Reviewer)
            .WithSummary("Renames a document or changes its expiry. Reviewer or Admin.")
            .Produces<CompanyDocumentDto>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        documents.MapDelete("/{docId:guid}", async (Guid id, Guid docId, ISender sender, CancellationToken ct) =>
            {
                await sender.Send(new DeleteCompanyDocumentCommand(id, docId), ct);
                return TypedResults.NoContent();
            })
            .RequireAuthorization(Policies.Admin)
            .WithSummary("Soft-deletes a document; the encrypted blob is kept. Admin.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }
}
