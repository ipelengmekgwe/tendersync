# Worker one-shot commands

The Worker runs a single job inline and exits when it is started with one of these flags. No Hangfire server starts, so nothing else runs alongside. Use them to backfill, to re-run a stage for specific tenders, or to measure something. In Docker, `deploy/docker/tendersync.ps1 ingest` and `seed-demo` wrap the common ones (docs/runbooks/local.md).

```bash
dotnet run --project src/TenderSync.Worker -- --ingest-days 7
```

```bash
dotnet run --project src/TenderSync.Worker -- --backfill-days 90
```

```bash
dotnet run --project src/TenderSync.Worker -- --probe
```

```bash
dotnet run --project src/TenderSync.Worker -- --download-documents ocds-9t57fa-175186,ocds-9t57fa-175322
```

The last one downloads and extracts the bid packs of the listed tenders inline and logs one line per document, which is how the WP04 acceptance rate was measured.

```bash
dotnet run --project src/TenderSync.Worker -- --classify-days 7 --prefilter-only
```

```bash
dotnet run --project src/TenderSync.Worker -- --classify ocds-9t57fa-175186,ocds-9t57fa-175322
```

```bash
dotnet run --project src/TenderSync.Worker -- --build-golden-set docs/golden/classification.jsonl --golden-days 30
```

`--classify-days` without `--prefilter-only` calls the model for every stage-1 candidate (about a quarter of releases, under a cent each on Haiku). The golden-set builder keeps existing labels when the file is rebuilt.

```bash
dotnet run --project src/TenderSync.Worker -- --extract ocds-9t57fa-175186 --extraction-template docs/golden/extraction
```

```bash
dotnet run --project src/TenderSync.Worker -- --migrate-seed
```

```bash
dotnet run --project src/TenderSync.Worker -- --qualify ocds-9t57fa-175186,ocds-9t57fa-175322 --qualify-report docs/golden/qualification
```

```bash
dotnet run --project src/TenderSync.Worker -- --refresh-active --compute-status --reminders
```

```bash
dotnet run --project src/TenderSync.Worker -- --seed-demo
```

`--refresh-active`, `--compute-status` and `--reminders` run the WP11 jobs once (refresh matched tenders from the portal, re-derive statuses, send the closing, briefing and document-expiry reminders). `--extract` runs requirement extraction for the listed tenders on the Strong tier (tens of cents per tender) and, with `--extraction-template`, writes the review templates. `--qualify` runs the qualification engine for the listed tenders (every matched profile, or every active one when nothing matched) and, with `--qualify-report`, writes each report's markdown for the five-tender acceptance review. `--migrate-seed` is the production migration job: it applies migrations and refreshes reference data. `--seed-demo` (Development only) writes a demo tender, `DEMO/E2E/0001`, with a three-page PDF bid pack, requirements sourced to pages 2 and 3, a qualification report and a New match. It uses the active company profile and creates "Demo Technologies (Pty) Ltd" when there is none. Rerunning it resets the match to New, and no events reach the pipeline or notifications.
