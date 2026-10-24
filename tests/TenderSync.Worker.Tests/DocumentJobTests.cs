using System.Net.Http.Json;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TenderSync.Application.Abstractions.Persistence;
using TenderSync.Domain.Enums;
using TenderSync.Domain.Events;
using TenderSync.Domain.Notifications;
using TenderSync.Domain.Tenders;
using TenderSync.Infrastructure.Persistence;
using TenderSync.Worker.Jobs;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace TenderSync.Worker.Tests;

/// <summary>Download and extraction jobs end to end: fake eTenders file server → filesystem store → document_texts. WP04 acceptance.</summary>
[Collection(WorkerCollection.Name)]
[Trait("Category", "Integration")]
public class DocumentJobTests(WorkerFactory factory)
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private static byte[] Pdf(params string[] pageTexts)
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        foreach (var text in pageTexts)
        {
            var page = builder.AddPage(UglyToad.PdfPig.Content.PageSize.A4);
            if (text.Length > 0)
            {
                page.AddText(text, 11, new UglyToad.PdfPig.Core.PdfPoint(40, 800), font);
            }
        }

        return builder.Build();
    }

    private async Task<Guid> InsertTenderAsync(params (string Blob, string Title, string Format)[] documents)
    {
        var ocid = $"ocds-9t57fa-{Random.Shared.Next(100000, 999999)}";
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();

        var buyer = await db.Buyers.GetByExternalIdAsync("167");
        if (buyer is null)
        {
            buyer = Buyer.Create("167", "ESKOM", BuyerType.StateOwnedEntity, Province.National);
            db.Buyers.Add(buyer);
        }

        var snapshot = new TenderSnapshot(
            ocid, ocid[^6..], $"E{ocid[^6..]}", "SAP support services", "Request for quotation for SAP support", "167", "ESKOM",
            ProcurementCategory.Services, ["services"], "open", "Request for Quotation", null, "ZAR", Now, Now.AddDays(21),
            $"https://www.etenders.gov.za/Home/TenderDetails?Id={ocid[^6..]}", $"{ocid}-release", Now, "compiled", "{}",
            documents.Select(d => new TenderDocumentSnapshot(d.Blob, d.Title, $"https://www.etenders.gov.za/home/Download?blobName={d.Blob}&downloadedFileName={Uri.EscapeDataString(d.Title)}", d.Format, "en", Now, Now)).ToList(),
            false);

        var tender = Tender.CreateFromSnapshot(snapshot, buyer.Id, Province.National, TenderSourceKind.ETendersOcds, Now);
        tender.DeriveStatus(Now);
        db.Tenders.Add(tender);
        await db.SaveChangesAsync();
        return tender.Id;
    }

    [Fact]
    public async Task Download_job_stores_files_chains_extraction_and_extraction_writes_page_texts()
    {
        var textPdf = $"text-{Guid.NewGuid():N}.pdf";
        var scannedPdf = $"scan-{Guid.NewGuid():N}.pdf";
        var missing = $"missing-{Guid.NewGuid():N}.pdf";
        factory.Downloads.Add(textPdf, Pdf("INVITATION TO BID: SAP support and maintenance services for Eskom", "Closing date 14 November 2026 at 10:00"), "application/pdf");
        factory.Downloads.Add(scannedPdf, Pdf(string.Empty, string.Empty), "application/pdf");
        var tenderId = await InsertTenderAsync((textPdf, "Bid Document.pdf", "pdf"), (scannedPdf, "Scanned annexure.pdf", "pdf"), (missing, "Gone.pdf", "pdf"), ("audio.mp3", "Briefing.mp3", "mp3"));

        var monitoring = factory.Services.GetRequiredService<JobStorage>().GetMonitoringApi();
        var pipelineBefore = monitoring.EnqueuedCount(JobQueues.Pipeline);

        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<DownloadTenderDocumentsJob>().ExecuteAsync(tenderId, CancellationToken.None);
        }

        List<TenderDocument> documents;
        using (var scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            documents = await context.TenderDocumentSet.AsNoTracking().Where(d => d.TenderId == tenderId).ToListAsync();
        }

        documents.Select(d => $"{d.Title}: {d.Status} {d.Error}").Should().HaveCount(4);

        var text = documents.Single(d => d.ExternalId == textPdf);
        var scanned = documents.Single(d => d.ExternalId == scannedPdf);
        text.Status.Should().Be(DocumentStatus.Downloaded);
        text.StoragePath.Should().EndWith($"/{text.Id}.pdf");
        text.Sha256.Should().HaveLength(64);
        File.Exists(Path.Combine(factory.DocumentRoot, text.StoragePath!.Replace('/', Path.DirectorySeparatorChar))).Should().BeTrue();
        scanned.Status.Should().Be(DocumentStatus.Downloaded);
        documents.Single(d => d.ExternalId == missing).Status.Should().Be(DocumentStatus.Failed);
        documents.Single(d => d.ExternalId == missing).Error.Should().Contain("HTTP 404");
        documents.Single(d => d.ExternalId == "audio.mp3").Status.Should().Be(DocumentStatus.Skipped);
        monitoring.EnqueuedCount(JobQueues.Pipeline).Should().Be(pipelineBefore + 2, "one extraction job per stored file is chained on the pipeline queue");

        using (var scope = factory.Services.CreateScope())
        {
            var job = scope.ServiceProvider.GetRequiredService<ExtractDocumentTextJob>();
            await job.ExecuteAsync(text.Id, CancellationToken.None);
            await job.ExecuteAsync(scanned.Id, CancellationToken.None);
        }

        using (var scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var extracted = await context.TenderDocumentSet.AsNoTracking().SingleAsync(d => d.Id == text.Id);
            extracted.Status.Should().Be(DocumentStatus.Extracted);
            extracted.PageCount.Should().Be(2);
            extracted.NeedsOcr.Should().BeFalse();
            var pages = await context.DocumentTextSet.AsNoTracking().Where(t => t.DocumentId == text.Id).OrderBy(t => t.Page).ToListAsync();
            pages.Select(p => p.Page).Should().Equal(1, 2);
            pages[0].Text.Should().Contain("SAP support and maintenance");
            pages[1].Text.Should().Contain("14 November 2026");

            var flagged = await context.TenderDocumentSet.AsNoTracking().SingleAsync(d => d.Id == scanned.Id);
            flagged.Status.Should().Be(DocumentStatus.Extracted);
            flagged.NeedsOcr.Should().BeTrue("blank pages average fewer than 50 chars");

            var runs = await context.JobRunSet.AsNoTracking().Where(r => r.JobName == nameof(DownloadTenderDocumentsJob) || r.JobName == nameof(ExtractDocumentTextJob)).ToListAsync();
            runs.Should().Contain(r => r.JobName == nameof(DownloadTenderDocumentsJob) && r.Success == true && r.ItemsProcessed == 2);
            runs.Count(r => r.JobName == nameof(ExtractDocumentTextJob) && r.Success == true).Should().BeGreaterThanOrEqualTo(2);
        }
    }

    [Fact]
    public async Task Re_running_the_download_job_skips_stored_files()
    {
        var blob = $"once-{Guid.NewGuid():N}.pdf";
        factory.Downloads.Add(blob, Pdf("only once"), "application/pdf");
        var tenderId = await InsertTenderAsync((blob, "Once.pdf", "pdf"));

        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<DownloadTenderDocumentsJob>().ExecuteAsync(tenderId, CancellationToken.None);
        }

        var requestsAfterFirst = factory.Downloads.Requests;
        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<DownloadTenderDocumentsJob>().ExecuteAsync(tenderId, CancellationToken.None);
        }

        factory.Downloads.Requests.Should().Be(requestsAfterFirst, "a stored document is not fetched again");
    }

    [Fact]
    public async Task A_documents_requested_event_enqueues_the_download_job_from_the_outbox()
    {
        var tenderId = await InsertTenderAsync(($"req-{Guid.NewGuid():N}.pdf", "Pack.pdf", "pdf"));
        var monitoring = factory.Services.GetRequiredService<JobStorage>().GetMonitoringApi();
        var before = monitoring.EnqueuedCount(JobQueues.Pipeline);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
            db.Outbox.Add(OutboxMessage.FromEvent(new TenderDocumentsRequested(tenderId, "ocds-9t57fa-000000", 1, Now)));
            await db.SaveChangesAsync();
        }

        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<ProcessOutboxJob>().ExecuteAsync(CancellationToken.None);
        }

        monitoring.EnqueuedCount(JobQueues.Pipeline).Should().BeGreaterThanOrEqualTo(before + 1, "other tests' TenderFirstSeen events in the outbox also enqueue classification jobs");
    }

    [Fact]
    public async Task Readiness_includes_the_document_store()
    {
        using var client = factory.CreateClient();

        var ready = await client.GetFromJsonAsync<System.Text.Json.JsonElement>("/health/ready");

        ready.GetProperty("status").GetString().Should().Be("Healthy");
        ready.GetProperty("checks").EnumerateArray().Select(c => c.GetProperty("name").GetString()).Should().Contain("document-store");
    }
}
