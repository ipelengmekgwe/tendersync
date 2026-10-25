# Sequence diagrams

These diagrams follow the code as of WP18, not the first sketch in PLAN.md §4.4. The main difference is that pipeline
stages do not enqueue each other: each stage saves its result and raises a domain event. The event becomes an
`outbox_messages` row in the same transaction. `ProcessOutboxJob` publishes it, and an event handler enqueues the next
job through `IPipelineScheduler`. All event handlers run in the Worker; the API registers `NoPipelineScheduler`.

Hangfire queues:

- **`ingest`, `notify` and `default`:** the Worker's main server.
- **`pipeline`:** a second server with `Hangfire:PipelineWorkerCount` workers (2 by default). It keeps document and model
  work to a polite concurrency.

Cron times are SAST (`JobSchedule.Register`).

- [Ingest](#ingest)
- [Match to notification](#match-to-notification)
- [Qualification](#qualification)
- [Reminders and status](#reminders-and-status)

## Ingest

`IngestRecentReleasesJob` runs every 30 minutes over a short rolling window. `BackfillReleasesJob` runs the same path
over 90 days in 7-day slices on Sunday at 02:00. Every job body runs inside `JobRunner`, which writes a `job_runs` row.
After three failures in a row, `JobRunner` records an unhealthy `SourceHealthCheck`, which raises `PipelineFailure`.

```mermaid
sequenceDiagram
  autonumber
  participant HF as Hangfire (ingest queue)
  participant J as IngestRecentReleasesJob
  participant TI as TenderIngestor
  participant SRC as ETendersOcdsSource
  participant EC as ETendersClient
  participant ET as eTenders OCDS API
  participant T as Tender (domain)
  participant DB as PostgreSQL
  participant OB as ProcessOutboxJob

  HF->>J: every 30 min
  J->>TI: IngestWindowAsync(today - RecentWindowDays, today + 1)
  TI->>SRC: FetchAsync(window)
  loop each page (PageSize 100)
    SRC->>EC: GetPagesAsync
    EC->>ET: GET api/OCDSReleases?PageNumber&PageSize=100&dateFrom&dateTo
    alt HTTP 500 (poison row on the page)
      EC->>ET: bisect 100 → 10 → 1, skip only the failing rows
    end
    ET-->>EC: release package
    SRC->>SRC: ReleaseSchemaValidator.Validate (drift is logged and counted)
    SRC->>SRC: ReleaseMapper.ToSnapshot → TenderSnapshot
  end
  SRC-->>TI: snapshots
  loop each snapshot
    TI->>TI: ProvinceResolver.Resolve + IMunicipalityLookup (buyer province and type)
    alt new OCID
      TI->>T: CreateFromSnapshot → raises TenderFirstSeen
    else known OCID
      TI->>T: ApplyRelease (ContentHasher, ReleaseDiff) → raises TenderChanged when the hash moved
    end
    T->>T: CancellationDetector, DeriveStatus → raises TenderStatusChanged on a transition
    TI->>DB: SaveChangesAsync (tender, releases, documents, outbox rows in one transaction)
  end
  J->>DB: job_runs row
  HF->>OB: every minute
  OB->>DB: unprocessed outbox_messages
  OB->>OB: publish DomainEventNotification
  Note over OB: TenderFirstSeen → ClassifyNewTenders → enqueue ClassifyTenderJob<br/>TenderChanged (title or description) → ReclassifyChangedTenders<br/>TenderChanged (documents, matched tender) → DownloadChangedDocumentsOfMatchedTenders<br/>TenderChanged (documents or closing date, matched) → NotifyOnTenderChanged
```

## Match to notification

```mermaid
sequenceDiagram
  autonumber
  participant HF as Hangfire
  participant CJ as ClassifyTenderJob (pipeline)
  participant TC as TenderClassifier
  participant KP as KeywordPrefilter
  participant LLM as GuardedLlmClient
  participant DB as PostgreSQL
  participant OB as Outbox handlers
  participant DJ as DownloadTenderDocumentsJob (pipeline)
  participant ND as NotificationDispatcher
  participant SJ as SendNotificationJob (notify)
  participant CH as Email / Telegram / Teams

  HF->>CJ: ClassifyTenderJob(tenderId)
  CJ->>TC: ClassifyAsync
  TC->>KP: Evaluate(TenderText, enabled categories)
  alt not a candidate
    TC->>DB: TenderClassification (stage Keyword), no match
  else candidate
    TC->>LLM: classify (fast model, prompt classify/vN)
    LLM->>LLM: LlmCostCap → router → adapter
    LLM->>DB: llm_calls row (tokens, cost)
    TC->>DB: TenderClassification (stage Llm)
    opt relevance ≥ MatchThreshold
      TC->>DB: TenderMatch.Create per active profile → raises TenderMatched
    end
  end
  OB->>DJ: TenderMatched → DownloadDocumentsWhenMatched
  OB->>ND: TenderMatched → NotifyOnNewMatch (urgent when closing within UrgentClosingDays)
  ND->>DB: enabled preferences that want NewMatch
  ND->>ND: dedupe key (type, channel, recipient, tender, profile, bucket)
  ND->>ND: render Scriban template per channel, quiet hours → NotBefore
  ND->>DB: Notification rows
  ND->>SJ: enqueue for rows due now
  SJ->>CH: NotificationSender.SendAsync
  CH-->>SJ: sent, skipped (channel not configured) or failed
  SJ->>DB: MarkSent / Skip / MarkFailed (backoff 1, 2, 4, 8 min)
  Note over SJ: SweepNotificationsJob (every minute) sends rows held by quiet hours, retries,<br/>and rows the API created (test messages)
  DJ->>DJ: TenderDocumentDownloader: throttled download, DocumentFormats.Sniff, SHA-256
  DJ->>DB: IDocumentStore.SaveAsync(tenders/{ocid}/{documentId}.{ext}), MarkDownloaded
  DJ->>HF: enqueue ExtractDocumentTextJob per downloaded document
```

## Qualification

```mermaid
sequenceDiagram
  autonumber
  participant XT as ExtractDocumentTextJob (pipeline)
  participant OB as Outbox handlers
  participant RJ as ExtractRequirementsJob (pipeline)
  participant RE as RequirementExtractor
  participant LLM as GuardedLlmClient
  participant QJ as QualifyTenderJob (pipeline)
  participant TQ as TenderQualifier
  participant DOM as Domain rules
  participant DB as PostgreSQL
  participant ND as NotificationDispatcher

  XT->>DB: CompositeTextExtractor pages → document_texts
  XT->>DB: TenderDocument.MarkExtracted → raises DocumentTextExtracted
  OB->>OB: ExtractWhenDocumentsAreReady: tender matched and no document still Pending or Downloaded
  OB->>RJ: enqueue (POST /tenders/{id}/extract → TenderExtractionRequested takes the same path)
  RJ->>RE: ExtractAsync
  RE->>DB: documents in order: main bid document first
  RE->>LLM: extract-requirements (strong model): native PDF parts with citations, or [page N] text
  RE->>RE: RequirementsMerger.Merge → TenderRequirement rows with source page and quote
  RE->>DB: ReplaceRequirements, summary, briefing, submission method
  RE->>DB: Tender.MarkRequirementsExtracted → raises RequirementsExtracted
  OB->>QJ: QualifyWhenRequirementsExtracted (POST /tenders/{id}/qualify → TenderQualificationRequested)
  QJ->>TQ: QualifyAsync(tenderId, profileId?)
  loop each profile (named, else matched, else active)
    TQ->>DOM: RequirementEvaluators per mandatory requirement (Pass / Fail / Expired / Unknown)
    opt certification wording the rules cannot match
      TQ->>LLM: fuzzy-match-certification (fast model)
    end
    TQ->>DOM: DocumentRequirementMatcher per required document (SBD/MBD forms are "to complete")
    opt functional criteria
      TQ->>LLM: estimate-functionality (strong model) - key personnel by role, only when the profile allows
    end
    TQ->>DOM: VerdictRules.Decide + Score
    TQ->>TQ: QualificationReportRenderer (disclaimer first, source links)
    TQ->>DB: QualificationReport.Create → raises QualificationCompleted
  end
  OB->>ND: NotifyOnQualificationCompleted (QualificationReady, bucket = verdict)
```

The model never decides a hard fact: CSD, tax, B-BBEE, CIDB, turnover, years and references are pure functions over
the profile in `TenderSync.Domain/Qualification`. Every Unknown, Expired, fuzzy match and compulsory briefing becomes a
question for a human in the report.

## Reminders and status

```mermaid
sequenceDiagram
  autonumber
  participant HF as Hangfire
  participant RS as ReminderService
  participant RF as TenderStatusRefresher
  participant TI as TenderIngestor
  participant SC as StatusComputer
  participant T as Tender (domain)
  participant DB as PostgreSQL
  participant OB as Outbox handlers
  participant ND as NotificationDispatcher

  HF->>RF: RefreshActiveTendersJob, daily 05:00 (ingest)
  RF->>DB: matched tenders not Awarded or Cancelled, least recently checked first
  loop each tender
    RF->>TI: RefreshAsync(ocid) → GET api/OCDSReleases/release/{ocid} → UpsertAsync (same path as ingest)
  end
  HF->>SC: ComputeStatusJob, hourly (pipeline)
  SC->>T: DeriveStatus(now, ClosingSoonDays) → raises TenderStatusChanged on a transition
  SC->>DB: SaveChanges
  OB->>ND: NotifyOnStatusChanged: Cancelled ("cancelled"), Closed ("closed:{date}"), Awarded (AwardPublished)
  HF->>RS: ClosingRemindersJob, daily 06:00 (notify)
  RS->>DB: non-dismissed matches, tender Open or ClosingSoon
  RS->>RS: ReminderBuckets.Closing → ClosingSoon7d / 3d / 1d
  RS->>ND: DispatchAsync(bucket = closing date in SAST, urgent for 1 day)
  HF->>RS: BriefingRemindersJob, daily 06:00
  RS->>ND: BriefingReminder (compulsory briefings also for dismissed matches, urgent)
  HF->>RS: DocumentExpiryCheckJob, daily 06:30
  RS->>ND: DocumentExpiring (bucket = document id + expiry date)
```

Buckets make the dedupe key: the same fact never notifies twice, a new fact (another reminder day, a new verdict, a
different diff) does. `Notification.dedupe_key` is unique in the database, so a retried outbox batch cannot double-send.
