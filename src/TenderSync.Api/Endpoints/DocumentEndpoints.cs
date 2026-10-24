using MediatR;
using TenderSync.Api.Security;
using TenderSync.Application.Features.Documents.Commands.RequestDocumentDownload;
using TenderSync.Application.Features.Documents.Queries.GetDocumentText;
using TenderSync.Application.Features.Documents.Queries.GetTenderDocumentFile;
using TenderSync.Application.Features.Documents.Queries.ListTenderDocuments;
using TenderSync.Application.Features.Tenders;

namespace TenderSync.Api.Endpoints;

/// <summary>Bid-pack documents of a tender (PLAN.md §10): list, stream a stored copy, read extracted text, trigger the download pipeline.</summary>
public static class DocumentEndpoints
{
    public static IEndpointRouteBuilder MapDocumentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/tenders/{id:guid}/documents")
            .WithTags("Documents")
            .RequireAuthorization(Policies.Viewer);

        group.MapGet("/", async (Guid id, ISender sender, CancellationToken ct) =>
                TypedResults.Ok(await sender.Send(new ListTenderDocumentsQuery(id), ct)))
            .WithSummary("Documents of a tender with their download and extraction state.")
            .Produces<IReadOnlyList<TenderDocumentDto>>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/{docId:guid}/download", async (Guid id, Guid docId, ISender sender, CancellationToken ct) =>
            {
                var file = await sender.Send(new GetTenderDocumentFileQuery(id, docId), ct);
                return Results.Stream(file.Content, file.ContentType, file.FileName, enableRangeProcessing: true);
            })
            .WithSummary("Streams the stored copy of a document. 409 when it has not been downloaded yet; the response then names the original URL.")
            .Produces(StatusCodes.Status200OK, contentType: "application/octet-stream")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/{docId:guid}/text", async (Guid id, Guid docId, ISender sender, CancellationToken ct) =>
                TypedResults.Ok(await sender.Send(new GetDocumentTextQuery(id, docId), ct)))
            .WithSummary("Extracted text per page (empty until the extraction job has run).")
            .Produces<DocumentTextDto>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/download", async (Guid id, ISender sender, CancellationToken ct) =>
                TypedResults.Accepted((string?)null, await sender.Send(new RequestDocumentDownloadCommand(id), ct)))
            .RequireAuthorization(Policies.Reviewer)
            .WithSummary("Queues the download and extraction of every document not yet stored. Reviewer or Admin.")
            .Produces<RequestDocumentDownloadResponse>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }
}
