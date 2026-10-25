using System.Globalization;
using Microsoft.EntityFrameworkCore;
using TenderSync.Application.Features.Classification;
using TenderSync.Application.Features.Classification.Golden;
using TenderSync.Application.Features.Documents;
using TenderSync.Application.Features.Qualification;
using TenderSync.Application.Features.Status;
using TenderSync.Application.Features.Requirements;
using TenderSync.Application.Features.Requirements.Golden;
using TenderSync.Infrastructure.Persistence;
using TenderSync.Infrastructure.Persistence.Seed;
using TenderSync.Worker.Jobs;

namespace TenderSync.Worker;

/// <summary>
/// One-shot command line modes for operators (PLAN.md WP03/WP04): <c>--ingest-days N</c> ingests the last N days and exits;
/// <c>--backfill-days N</c> does the same in weekly slices; <c>--probe</c> runs the source health check;
/// <c>--download-documents ocid[,ocid...]</c> downloads and extracts the bid packs of those tenders inline (no Hangfire);
/// <c>--classify-days N</c> or <c>--classify ocid[,ocid...]</c> runs classification (add <c>--prefilter-only</c> to skip the model);
/// <c>--build-golden-set path [--golden-days N]</c> writes the classification review file (WP07);
/// <c>--migrate-seed</c> applies migrations and reference-data seeding (the production migration job, PLAN.md §15);
/// <c>--extract ocid[,ocid...] [--extraction-template docs/golden/extraction]</c> extracts requirements (WP08) and writes review templates;
/// <c>--qualify ocid[,ocid...] [--qualify-report dir]</c> runs the qualification engine (WP09) and optionally writes each report's markdown;
/// <c>--refresh-active</c>, <c>--compute-status</c> and <c>--reminders</c> run the WP11 status refresh, status derivation and the three reminder passes;
/// <c>--seed-demo</c> writes the demo tender, PDF and match the dashboard e2e test uses (Development only, see <see cref="DemoDataSeeder"/>).
/// The Hangfire server is not started in these modes.
/// </summary>
public sealed record WorkerCli(
    int? IngestDays,
    int? BackfillDays,
    bool Probe,
    IReadOnlyList<string> DownloadDocumentsFor,
    int? ClassifyDays,
    IReadOnlyList<string> ClassifyOcids,
    bool PrefilterOnly,
    string? GoldenSetPath,
    int GoldenDays,
    bool MigrateAndSeed,
    IReadOnlyList<string> ExtractOcids,
    string? ExtractionTemplatePath,
    IReadOnlyList<string> QualifyOcids,
    string? QualifyReportPath,
    bool RefreshActive,
    bool ComputeStatus,
    bool Reminders,
    bool SeedDemo = false)
{
    public bool IsOneShot => IngestDays is not null || BackfillDays is not null || Probe || DownloadDocumentsFor.Count > 0 || ClassifyDays is not null || ClassifyOcids.Count > 0 || GoldenSetPath is not null || MigrateAndSeed || ExtractOcids.Count > 0 || QualifyOcids.Count > 0 || RefreshActive || ComputeStatus || Reminders || SeedDemo;

    public static WorkerCli Parse(string[] args)
    {
        int? ingest = null, backfill = null, classifyDays = null;
        var probe = false;
        var prefilterOnly = false;
        var ocids = new List<string>();
        var classify = new List<string>();
        string? golden = null;
        var goldenDays = 30;
        var migrateSeed = false;
        var extract = new List<string>();
        string? extractionTemplate = null;
        var qualify = new List<string>();
        string? qualifyReport = null;
        bool refreshActive = false, computeStatus = false, reminders = false, seedDemo = false;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--ingest-days" when i + 1 < args.Length && int.TryParse(args[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var d):
                    ingest = d;
                    i++;
                    break;
                case "--backfill-days" when i + 1 < args.Length && int.TryParse(args[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var b):
                    backfill = b;
                    i++;
                    break;
                case "--probe":
                    probe = true;
                    break;
                case "--download-documents" when i + 1 < args.Length:
                    ocids.AddRange(args[i + 1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                    i++;
                    break;
                case "--classify-days" when i + 1 < args.Length && int.TryParse(args[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var c):
                    classifyDays = c;
                    i++;
                    break;
                case "--classify" when i + 1 < args.Length:
                    classify.AddRange(args[i + 1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                    i++;
                    break;
                case "--prefilter-only":
                    prefilterOnly = true;
                    break;
                case "--build-golden-set" when i + 1 < args.Length:
                    golden = args[i + 1];
                    i++;
                    break;
                case "--golden-days" when i + 1 < args.Length && int.TryParse(args[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var g):
                    goldenDays = g;
                    i++;
                    break;
                case "--migrate-seed":
                    migrateSeed = true;
                    break;
                case "--extract" when i + 1 < args.Length:
                    extract.AddRange(args[i + 1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                    i++;
                    break;
                case "--extraction-template" when i + 1 < args.Length:
                    extractionTemplate = args[i + 1];
                    i++;
                    break;
                case "--qualify" when i + 1 < args.Length:
                    qualify.AddRange(args[i + 1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                    i++;
                    break;
                case "--qualify-report" when i + 1 < args.Length:
                    qualifyReport = args[i + 1];
                    i++;
                    break;
                case "--refresh-active":
                    refreshActive = true;
                    break;
                case "--compute-status":
                    computeStatus = true;
                    break;
                case "--reminders":
                    reminders = true;
                    break;
                case "--seed-demo":
                    seedDemo = true;
                    break;
            }
        }

        return new WorkerCli(ingest, backfill, probe, ocids, classifyDays, classify, prefilterOnly, golden, goldenDays, migrateSeed, extract, extractionTemplate, qualify, qualifyReport, refreshActive, computeStatus, reminders, seedDemo);
    }

    public async Task RunAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        using var scope = services.CreateScope();

        if (MigrateAndSeed)
        {
            // PLAN.md §15: production runs migrations from a one-shot job rather than on API start.
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync(cancellationToken);
            await scope.ServiceProvider.GetRequiredService<DatabaseSeeder>().SeedAsync(cancellationToken);
        }

        if (SeedDemo)
        {
            var environment = scope.ServiceProvider.GetRequiredService<IHostEnvironment>();
            if (!environment.IsDevelopment())
            {
                throw new InvalidOperationException($"--seed-demo writes demo data and only runs in Development (this is {environment.EnvironmentName}).");
            }

            var demo = await scope.ServiceProvider.GetRequiredService<DemoDataSeeder>().SeedAsync(cancellationToken);
            scope.ServiceProvider.GetRequiredService<ILogger<WorkerCli>>().LogInformation(
                "Demo tender {BidNumber}: /tenders/{TenderId} (match {MatchId}, profile {ProfileId}{Created})",
                DemoDataSeeder.DemoBidNumber, demo.TenderId, demo.MatchId, demo.CompanyProfileId, demo.ProfileCreated ? ", created" : string.Empty);
        }

        if (Probe)
        {
            await scope.ServiceProvider.GetRequiredService<SourceHealthCheckJob>().ExecuteAsync(cancellationToken);
        }

        if (IngestDays is { } days)
        {
            await scope.ServiceProvider.GetRequiredService<IngestRecentReleasesJob>().ExecuteAsync(days, cancellationToken);
        }

        if (BackfillDays is { } backfillDays)
        {
            await scope.ServiceProvider.GetRequiredService<BackfillReleasesJob>().ExecuteAsync(backfillDays, cancellationToken);
        }

        if (DownloadDocumentsFor.Count > 0)
        {
            await DownloadDocumentsAsync(scope.ServiceProvider, cancellationToken);
        }

        if (ClassifyDays is not null || ClassifyOcids.Count > 0)
        {
            await ClassifyAsync(scope.ServiceProvider, cancellationToken);
        }

        if (ExtractOcids.Count > 0)
        {
            await ExtractAsync(scope.ServiceProvider, cancellationToken);
        }

        if (QualifyOcids.Count > 0)
        {
            await QualifyAsync(scope.ServiceProvider, cancellationToken);
        }

        if (RefreshActive)
        {
            await scope.ServiceProvider.GetRequiredService<RefreshActiveTendersJob>().ExecuteAsync(cancellationToken);
        }

        if (ComputeStatus)
        {
            await scope.ServiceProvider.GetRequiredService<ComputeStatusJob>().ExecuteAsync(cancellationToken);
        }

        if (Reminders)
        {
            await scope.ServiceProvider.GetRequiredService<ClosingRemindersJob>().ExecuteAsync(cancellationToken);
            await scope.ServiceProvider.GetRequiredService<BriefingRemindersJob>().ExecuteAsync(cancellationToken);
            await scope.ServiceProvider.GetRequiredService<DocumentExpiryCheckJob>().ExecuteAsync(cancellationToken);
        }

        if (GoldenSetPath is not null)
        {
            var summary = await scope.ServiceProvider.GetRequiredService<GoldenSetBuilder>().BuildAsync(GoldenSetPath, GoldenDays, targetRows: 250, cancellationToken);
            scope.ServiceProvider.GetRequiredService<ILogger<WorkerCli>>().LogInformation(
                "Golden set {Path}: scanned {Scanned} tender(s), {Candidates} prefilter candidate(s), wrote {Rows} row(s), {Labelled} labelled",
                summary.Path, summary.TendersScanned, summary.PrefilterCandidates, summary.RowsWritten, summary.Labelled);
        }
    }

    /// <summary>
    /// Runs requirement extraction for the listed tenders (model calls on the Strong tier) and, with --extraction-template DIR,
    /// writes or refreshes docs/golden/extraction/{ocid}.json with the extracted values beside empty labels for review.
    /// </summary>
    private async Task ExtractAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var context = services.GetRequiredService<AppDbContext>();
        var extractor = services.GetRequiredService<RequirementExtractor>();
        var db = services.GetRequiredService<Application.Abstractions.Persistence.IAppDbContext>();
        var logger = services.GetRequiredService<ILogger<WorkerCli>>();

        var tenders = await context.TenderSet.AsNoTracking().Where(t => ExtractOcids.Contains(t.Ocid)).Select(t => new { t.Id, t.Ocid, t.BidNumber, t.Title }).ToListAsync(cancellationToken);
        decimal cost = 0m;
        int done = 0, failed = 0;
        foreach (var tender in tenders)
        {
            try
            {
                var outcome = await extractor.ExtractAsync(tender.Id, cancellationToken);
                cost += outcome.CostUsd;
                done++;
                logger.LogInformation("{Ocid}: {Count} requirement(s) from {Docs} document(s), {Verify} to verify, ${Cost} {Errors}", outcome.Ocid, outcome.Requirements, outcome.DocumentsProcessed, outcome.NeedsVerification, outcome.CostUsd, string.Join("; ", outcome.Errors));

                if (ExtractionTemplatePath is not null)
                {
                    var stored = await db.Tenders.GetWithDetailsAsync(tender.Id, cancellationToken);
                    var rows = await db.Tenders.GetRequirementsAsync(tender.Id, cancellationToken);
                    var path = Path.Combine(ExtractionTemplatePath, $"{tender.Ocid}.json");
                    var existing = File.Exists(path) ? System.Text.Json.JsonSerializer.Deserialize<ExtractionGoldenFile>(File.ReadAllText(path), ExtractionAccuracy.Json) : null;
                    var file = new ExtractionGoldenFile(tender.Ocid, tender.BidNumber, tender.Title, existing?.Labels ?? ExtractionLabels.Empty, ExtractionAccuracy.FromStored(stored!, rows), existing?.Note);
                    Directory.CreateDirectory(ExtractionTemplatePath);
                    File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(file, ExtractionAccuracy.Json));
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failed++;
                logger.LogError(ex, "Extraction of {Ocid} failed", tender.Ocid);
            }
        }

        logger.LogInformation("Extracted {Done} tender(s), {Failed} failed, ${Cost} total, ${Average} average", done, failed, cost, done == 0 ? 0 : cost / done);
    }

    /// <summary>Runs the qualification engine for the listed tenders (every matched or active profile) and, with --qualify-report DIR, writes {ocid}-{profile}.md for review.</summary>
    private async Task QualifyAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var context = services.GetRequiredService<AppDbContext>();
        var qualifier = services.GetRequiredService<TenderQualifier>();
        var db = services.GetRequiredService<Application.Abstractions.Persistence.IAppDbContext>();
        var logger = services.GetRequiredService<ILogger<WorkerCli>>();

        var tenders = await context.TenderSet.AsNoTracking().Where(t => QualifyOcids.Contains(t.Ocid)).Select(t => new { t.Id, t.Ocid }).ToListAsync(cancellationToken);
        foreach (var missing in QualifyOcids.Except(tenders.Select(t => t.Ocid), StringComparer.Ordinal))
        {
            logger.LogWarning("Tender {Ocid} is not in the database; ingest it first", missing);
        }

        decimal cost = 0m;
        int done = 0, failed = 0;
        foreach (var tender in tenders)
        {
            try
            {
                var outcome = await qualifier.QualifyAsync(tender.Id, null, cancellationToken);
                cost += outcome.CostUsd;
                done++;
                foreach (var profile in outcome.Profiles)
                {
                    logger.LogInformation("{Ocid} for {Company}: {Verdict}, score {Score}, functionality {Functionality}, {Questions} question(s), ${Cost}",
                        outcome.Ocid, profile.CompanyName, profile.Verdict, profile.Score, profile.EstimatedFunctionality, profile.Questions, profile.CostUsd);
                    if (QualifyReportPath is not null)
                    {
                        var report = await db.QualificationReports.GetByIdAsync(profile.ReportId, cancellationToken);
                        Directory.CreateDirectory(QualifyReportPath);
                        var safeName = string.Concat(profile.CompanyName.Where(char.IsLetterOrDigit));
                        await File.WriteAllTextAsync(Path.Combine(QualifyReportPath, $"{outcome.Ocid}-{safeName}.md"), report!.SummaryMarkdown, cancellationToken);
                    }
                }

                foreach (var error in outcome.Errors)
                {
                    logger.LogWarning("{Ocid}: {Error}", outcome.Ocid, error);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failed++;
                logger.LogError(ex, "Qualification of {Ocid} failed", tender.Ocid);
            }
        }

        logger.LogInformation("Qualified {Done} tender(s), {Failed} failed, ${Cost} total", done, failed, cost);
    }

    /// <summary>Classifies tenders by ocid or by publication window; --prefilter-only runs stage 1 alone (no model, no cost).</summary>
    private async Task ClassifyAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var context = services.GetRequiredService<AppDbContext>();
        var classifier = services.GetRequiredService<TenderClassifier>();
        var logger = services.GetRequiredService<ILogger<WorkerCli>>();

        var query = context.TenderSet.AsNoTracking();
        query = ClassifyOcids.Count > 0 ? query.Where(t => ClassifyOcids.Contains(t.Ocid)) : query.Where(t => t.PublishedAt >= DateTimeOffset.UtcNow.AddDays(-ClassifyDays!.Value));
        var ids = await query.OrderBy(t => t.PublishedAt).Select(t => new { t.Id, t.Ocid }).ToListAsync(cancellationToken);

        int candidates = 0, llm = 0, matches = 0, nearMisses = 0, failed = 0;
        decimal cost = 0m;
        foreach (var tender in ids)
        {
            try
            {
                var outcome = await classifier.ClassifyAsync(tender.Id, PrefilterOnly, cancellationToken);
                candidates += outcome.Candidate ? 1 : 0;
                llm += outcome.Stage == Domain.Enums.ClassificationStage.Llm ? 1 : 0;
                matches += outcome.MatchesCreated;
                nearMisses += outcome.IsNearMiss ? 1 : 0;
                cost += outcome.CostUsd;
                if (outcome.Candidate)
                {
                    logger.LogInformation("{Ocid} {Stage} relevance {Relevance} [{Codes}] {Reason}", outcome.Ocid, outcome.Stage, outcome.Relevance, string.Join(",", outcome.CategoryCodes), outcome.Reason);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failed++;
                logger.LogError(ex, "Classification of {Ocid} failed", tender.Ocid);
            }
        }

        logger.LogInformation(
            "Classified {Total} tender(s): {Candidates} stage-1 candidates, {Llm} model calls, {Matches} new match(es), {NearMisses} near miss(es), {Failed} failed, ${Cost}",
            ids.Count, candidates, llm, matches, nearMisses, failed, cost);
    }

    /// <summary>Download then extract, synchronously, printing one line per document so an operator can judge the acceptance rate.</summary>
    private async Task DownloadDocumentsAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var context = services.GetRequiredService<AppDbContext>();
        var downloader = services.GetRequiredService<TenderDocumentDownloader>();
        var extraction = services.GetRequiredService<DocumentTextExtraction>();
        var logger = services.GetRequiredService<ILogger<WorkerCli>>();

        var ids = await context.TenderSet.AsNoTracking().Where(t => DownloadDocumentsFor.Contains(t.Ocid)).Select(t => new { t.Id, t.Ocid }).ToListAsync(cancellationToken);
        foreach (var missing in DownloadDocumentsFor.Except(ids.Select(t => t.Ocid), StringComparer.Ordinal))
        {
            logger.LogWarning("Tender {Ocid} is not in the database; ingest it first", missing);
        }

        int documents = 0, downloaded = 0, extracted = 0, needsOcr = 0, skipped = 0, failed = 0;
        foreach (var tender in ids)
        {
            var summary = await downloader.DownloadAsync(tender.Id, cancellationToken);
            documents += summary.Attempted;
            downloaded += summary.Downloaded;
            skipped += summary.Skipped;
            failed += summary.Failed;
            foreach (var error in summary.Errors)
            {
                logger.LogWarning("{Ocid}: {Error}", tender.Ocid, error);
            }

            foreach (var documentId in summary.DownloadedDocumentIds)
            {
                var result = await extraction.ExtractAsync(documentId, cancellationToken);
                switch (result.Status)
                {
                    case Domain.Enums.DocumentStatus.Extracted:
                        extracted++;
                        needsOcr += result.NeedsOcr ? 1 : 0;
                        break;
                    case Domain.Enums.DocumentStatus.Skipped:
                        skipped++;
                        break;
                    default:
                        failed++;
                        break;
                }

                logger.LogInformation("{Ocid} {DocumentId}: {Status}, {Pages} page(s), {Chars} chars, needs OCR {NeedsOcr} {Error}", tender.Ocid, documentId, result.Status, result.Pages, result.Chars, result.NeedsOcr, result.Error);
            }
        }

        logger.LogInformation(
            "Documents for {Tenders} tender(s): {Documents} pending, {Downloaded} downloaded, {Extracted} extracted ({NeedsOcr} need OCR), {Skipped} skipped, {Failed} failed",
            ids.Count, documents, downloaded, extracted, needsOcr, skipped, failed);
    }
}
