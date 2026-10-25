# Load sanity: 90-day backfill

PLAN.md §17.3 (WP18) asks for a timing of the 90-day ingest backfill. This is the run behind that figure, how to repeat
it, and what it showed.

## Run

| | |
|---|---|
| Date | 25 October 2026, 18:12 to 19:34 SAST |
| Command | `TenderSync.Worker --backfill-days 90` (Release build, Development settings) |
| Database | A fresh `tendersync_loadtest` database in the Compose PostgreSQL 17 container, migrated with `--migrate-seed`, on a Windows 11 laptop |
| Window | 27 July to 26 October 2026, in 13 slices of 7 days (`ETenders:BackfillSliceDays`) |

## Results

| Measure | Value |
|---|---|
| Wall time | **82 minutes** (4,939 s), about 6.3 minutes per weekly slice |
| Releases fetched | 9,377 |
| Tenders created | 8,157, from 583 buyers |
| Repeated releases | 1,220 counted as "unchanged". Releases near a slice boundary appear in both slices, and the upsert is idempotent |
| Documents listed | 21,354; none downloaded, since the backfill only reads the feed |
| Releases skipped (eTenders answers HTTP 500) | **2,027**, rising from 11 in the newest week to 265 in the oldest |
| HTTP requests | 6,741, of which 3,601 were HTTP 500 during bisection. Median 191 ms, p95 580 ms, slowest 5.3 s |
| Database size afterwards | 54 MB, raw release JSON included |
| Failures | None: every slice completed and the job recorded success |

## What it shows

- **Time goes to bisection, not to the database.** A healthy page of 100 releases takes a few hundred milliseconds.
  A page with a "poison" release fails as a whole, and `ETendersClient` then re-fetches it in pages of 10 and 1 to skip
  only the bad rows. More than half of all requests were these retries, and older slices have far more of them.
- **The skip rate rises with age.** About 1.5% of releases were skipped in the newest week, about 31% in the oldest.
  The recent 3-day ingest every 30 minutes is barely affected. A backfill, though, misses a meaningful share of older
  releases, and those tenders never appear. This is a limit of the source, not of the client. The skips are counted in
  each job run's details (`SourceFetchStats`). Follow-up: report the skip ratio on `/admin/pipeline`, and retry skipped
  offsets on a later run, since some releases may become readable again.
- **The weekly schedule fits.** `BackfillReleasesJob` runs on Sunday at 02:00 SAST on the `ingest` queue. An 82-minute
  run sits well inside the night. The 30-minute recent ingest uses the same queue and waits behind it, which is
  acceptable once a week.
- **Storage is small.** About 6.6 KB per tender including raw JSON, so a year of tenders is roughly 250 to 300 MB before
  any documents are downloaded. Documents are fetched only for matched tenders.

## Repeating it

The run writes to its own database, so your development data is untouched. The connection string below uses the
Compose defaults.

```bash
docker exec tendersync-postgres psql -U tendersync -d postgres -c "create database tendersync_loadtest"
```

```bash
dotnet build -c Release src/TenderSync.Worker
```

Run the next two from `src/TenderSync.Worker/bin/Release/net10.0` so the Worker finds its settings:

```bash
ASPNETCORE_ENVIRONMENT=Development ConnectionStrings__TenderSync="Host=localhost;Port=5432;Database=tendersync_loadtest;Username=tendersync;Password=tendersync" dotnet TenderSync.Worker.dll --migrate-seed
```

```bash
ASPNETCORE_ENVIRONMENT=Development ConnectionStrings__TenderSync="Host=localhost;Port=5432;Database=tendersync_loadtest;Username=tendersync;Password=tendersync" dotnet TenderSync.Worker.dll --backfill-days 90
```

The job logs one `Ingest {from}..{to}` line per slice with fetched, created, unchanged and skipped counts. Drop the
database when you are done:

```bash
docker exec tendersync-postgres psql -U tendersync -d postgres -c "drop database tendersync_loadtest"
```
