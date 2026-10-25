# Domain class diagrams

One diagram per bounded area, drawn from `src/TenderSync.Domain` (and the Identity types in Infrastructure). PLAN.md
§4.5 is the original sketch; where they differ, this page and the code win.

Conventions that apply everywhere:

- **`Entity` is the base type.** Every persisted type except `DocumentText`, `TenderClassification` and the Identity
  types derives from it. `Entity` provides:
  - `Id`, a UUIDv7 generated in the domain.
  - `CreatedAt` and `UpdatedAt` timestamps.
  - Domain events, raised with `Raise(...)`.
- **There is no separate aggregate-root type.** The aggregates are the entities that own child collections, such as
  `Tender`.
- **Events are written in the same transaction.** On `SaveChangesAsync`, `AuditAndOutboxInterceptor` turns raised events
  into `outbox_messages` rows.
- **Setters are private.** State changes only through methods such as `Tender.ApplyRelease` and `TenderMatch.Dismiss`.
- **JSON columns:** properties named `*Json` are jsonb columns with a typed accessor that EF ignores.

- [Tenders and sources](#tenders-and-sources)
- [Classification and matching](#classification-and-matching)
- [Requirements and qualification](#requirements-and-qualification)
- [Company](#company)
- [Notifications](#notifications)
- [Operations and security](#operations-and-security)

## Tenders and sources

```mermaid
classDiagram
  direction LR
  class Buyer {
    +string ExternalId
    +string Name
    +BuyerType Type
    +Province Province
    +Create(externalId, name, type, province)$ Buyer
    +Update(name, type, province)
  }
  class Tender {
    +string Ocid
    +string BidNumber
    +string Title
    +string Description
    +Guid BuyerId
    +Province Province
    +ProcurementCategory MainCategory
    +DateTimeOffset PublishedAt
    +DateTimeOffset? ClosingAt
    +DateTimeOffset? BriefingAt
    +bool? BriefingCompulsory
    +SubmissionMethod SubmissionMethod
    +TenderStatus Status
    +string SourceUrl
    +string ContentHash
    +bool HasAwards
    +DateTimeOffset? LastCheckedAt
    +CreateFromSnapshot(snapshot, buyerId, province, source, now)$ Tender
    +ApplyRelease(snapshot, province, now) ReleaseDiff
    +DeriveStatus(now, closingSoonWindow) TenderStatus
    +MarkAwarded(now)
    +MarkCancelled(reason, now)
    +RequestDocumentDownload(now) int
    +RequestRequirementExtraction(now)
    +MarkRequirementsExtracted(count, now)
    +RequestQualification(companyProfileId, now)
  }
  class TenderRelease {
    +string ReleaseId
    +DateTimeOffset ReleaseDate
    +string Tag
    +string RawJson
    +string Hash
  }
  class TenderDocument {
    +string ExternalId
    +string Title
    +string Url
    +DocumentStatus Status
    +string? StoragePath
    +string? Sha256
    +long? SizeBytes
    +int? PageCount
    +bool NeedsOcr
    +MarkDownloaded(path, sha256, size, now)
    +MarkExtracted(pageCount, needsOcr, now)
    +MarkFailed(error)
    +MarkSkipped(reason)
  }
  class DocumentText {
    +Guid DocumentId
    +int Page
    +string Text
    +int CharCount
  }
  class TenderStatusHistory {
    +TenderStatus FromStatus
    +TenderStatus ToStatus
    +string? Reason
    +DateTimeOffset At
  }
  class ReleaseDiff {
    <<value object>>
    +bool IsNew
    +IReadOnlyList~string~ NewDocumentIds
    +IReadOnlyList~string~ ModifiedDocumentIds
    +DateTimeOffset? OldClosingAt
    +DateTimeOffset? NewClosingAt
    +bool TitleChanged
    +bool AwardsPublished
    +bool ClosingChanged
  }
  class Municipality {
    +string Code
    +string Name
    +MunicipalityType Type
    +Province Province
  }
  class ProvinceResolver {
    <<static>>
    +Resolve(buyerName, municipalityLookup)$ BuyerResolution
  }
  class CancellationDetector {
    <<static>>
    +Detect(snapshot)$ string?
  }
  class SourceHealthCheck {
    +string Source
    +bool Ok
    +int? HttpStatus
    +string? SchemaDriftJson
    +Record(...)$ SourceHealthCheck
  }
  class TenderStatus {
    <<enumeration>>
    Unknown
    Open
    ClosingSoon
    Closed
    Awarded
    Cancelled
  }
  Buyer "1" --> "*" Tender
  Tender "1" *-- "*" TenderRelease
  Tender "1" *-- "*" TenderDocument
  Tender "1" *-- "*" TenderStatusHistory
  TenderDocument "1" *-- "*" DocumentText
  Tender ..> ReleaseDiff : ApplyRelease returns
  Tender ..> CancellationDetector : uses
  ProvinceResolver ..> Municipality : looks up
  Tender --> TenderStatus
```

Events raised here:

- **`TenderFirstSeen`:** a new OCID.
- **`TenderChanged`:** carries the `ReleaseDiff`.
- **`TenderStatusChanged`:** every transition, recorded in `TenderStatusHistory`. Awarded and Cancelled are sticky.
- **`TenderDocumentsRequested`, `TenderExtractionRequested`, `RequirementsExtracted` and
  `TenderQualificationRequested`:** pipeline hand-offs.
- **`DocumentTextExtracted`:** a document's text is stored.
- **`PipelineFailure`:** raised by `SourceHealthCheck.Record` when a check fails or the schema drifts.

## Classification and matching

```mermaid
classDiagram
  direction LR
  class InterestCategory {
    +string Code
    +string Name
    +string[] Keywords
    +string[] NegativeKeywords
    +bool Enabled
    +int Weight
    +Create(code, name, description, keywords, negativeKeywords, weight)$ InterestCategory
    +Update(name, description, keywords, negativeKeywords, weight, enabled)
  }
  class TenderClassification {
    +Guid TenderId
    +ClassificationStage Stage
    +string[] CategoryCodes
    +int Relevance
    +string? Rationale
    +string? Model
    +string? PromptVersion
    +ApplyKeywordResult(codes, relevance, rationale, now)
    +ApplyLlmResult(codes, relevance, rationale, model, promptVersion, tokensIn, tokensOut, now)
  }
  class TenderMatch {
    +Guid TenderId
    +Guid CompanyProfileId
    +int Relevance
    +MatchStatus Status
    +string? DismissReason
    +string? Notes
    +Create(tenderId, companyProfileId, relevance, now)$ TenderMatch
    +SetStatus(status)
    +Dismiss(reason, now)
    +SetNotes(notes)
  }
  class KeywordPrefilter {
    <<Application service>>
    +Evaluate(TenderText, categories) PrefilterResult
    +Matches(keyword, text)$ bool
  }
  class TenderClassifier {
    <<Application service>>
    +ClassifyAsync(tenderId, prefilterOnly) 
  }
  class MatchStatus {
    <<enumeration>>
    New
    Reviewing
    Dismissed
    Applying
    Applied
    Won
    Lost
    Expired
  }
  class ClassificationStage {
    <<enumeration>>
    None
    Keyword
    Llm
  }
  Tender "1" -- "0..1" TenderClassification
  Tender "1" -- "*" TenderMatch
  CompanyProfile "1" -- "*" TenderMatch
  TenderClassification ..> InterestCategory : CategoryCodes (by code, no FK)
  TenderClassifier ..> KeywordPrefilter : stage 1
  TenderClassifier ..> TenderClassification : writes
  TenderClassifier ..> TenderMatch : creates when relevance ≥ MatchThreshold
  TenderMatch --> MatchStatus
  TenderClassification --> ClassificationStage
```

`TenderMatch.Create` raises `TenderMatched`. The match's workflow state changes only through `UpdateMatchCommand` (PATCH
`/matches/{id}`), and dismissing needs a reason.

## Requirements and qualification

```mermaid
classDiagram
  direction LR
  class TenderRequirement {
    +Guid TenderId
    +RequirementKind Kind
    +RequirementType Type
    +string Text
    +string? ValueJson
    +bool Mandatory
    +decimal? Weight
    +decimal? Threshold
    +RequirementSource? Source
    +decimal Confidence
    +string Model
    +string PromptVersion
    +bool NeedsVerification
  }
  class RequirementSource {
    <<value object>>
    +Guid? DocumentId
    +int? Page
    +string? Quote
  }
  class QualificationReport {
    +Guid TenderId
    +Guid CompanyProfileId
    +QualificationVerdict Verdict
    +int Score
    +int? EstimatedFunctionalityScore
    +string FindingsJson
    +string MissingMandatoryJson
    +string MissingDocumentsJson
    +string QuestionsJson
    +string SummaryMarkdown
    +Create(...)$ QualificationReport
  }
  class QualificationFinding {
    <<record>>
    +Guid? RequirementId
    +RequirementType Type
    +FindingOutcome Outcome
    +string Detail
    +decimal Confidence
  }
  class IRequirementEvaluator {
    <<interface>>
    +RequirementType Type
    +Evaluate(EvaluationContext) RequirementEvaluation
  }
  class RequirementEvaluators {
    <<static>>
    +For(type)$ IRequirementEvaluator
    +Evaluate(context)$ RequirementEvaluation
  }
  class RequirementValueParser {
    <<static>>
    +BbbeeLevel(text)$ int?
    +Rand(text)$ decimal?
    +CidbGrade(text)$ (int, string?)?
    +References(text)$ ReferenceSpec
  }
  class DocumentRequirementMatcher {
    <<static>>
    +Check(name, formCode, requirementId, documents, validityPoint)$ DocumentCheck
  }
  class VerdictRules {
    <<static>>
    +Decide(mandatory, estFunctionality, threshold)$ QualificationVerdict
    +Score(mandatory, estFunctionality, documents, weights)$ int
  }
  class QualificationVerdict {
    <<enumeration>>
    Unknown
    Qualify
    Partial
    NotQualify
  }
  class FindingOutcome {
    <<enumeration>>
    Unknown
    Pass
    Fail
    Expired
  }
  Tender "1" -- "*" TenderRequirement
  TenderRequirement *-- "0..1" RequirementSource
  Tender "1" -- "*" QualificationReport
  CompanyProfile "1" -- "*" QualificationReport
  QualificationReport *-- "*" QualificationFinding
  RequirementEvaluators o-- "*" IRequirementEvaluator
  IRequirementEvaluator ..> RequirementValueParser : reads thresholds
  IRequirementEvaluator ..> CompanyProfile : evaluates
  DocumentRequirementMatcher ..> CompanyDocument : checks
  VerdictRules ..> QualificationFinding
  QualificationReport --> QualificationVerdict
  QualificationFinding --> FindingOutcome
```

The evaluators cover CSD, tax compliance, B-BBEE, CIDB, company registration, years in business, minimum turnover,
references and certifications. Insurance, accreditation, local content and joint-venture rules are evaluated as
Unknown, because the profile does not hold them, and become questions. `VerdictRules` decide the outcome:

- Any Fail gives NotQualify.
- Any Unknown or Expired, or an estimate under the threshold, gives Partial.
- Anything else gives Qualify.

The score weights are 60 mandatory, 30 functionality and 10 documents. `QualificationReport.Create` raises
`QualificationCompleted`.

## Company

```mermaid
classDiagram
  direction LR
  class CompanyProfile {
    +string Name
    +string? RegistrationNumber
    +string? CsdNumber
    +DateTimeOffset? CsdExpiry
    +string? TaxPin
    +DateTimeOffset? TaxPinExpiry
    +int? BbbeeLevel
    +DateTimeOffset? BbbeeExpiry
    +int? CidbGrade
    +string[] CidbClasses
    +Province[] Provinces
    +int YearsTrading
    +decimal? AnnualTurnover
    +string CapabilitiesJson
    +bool IsActive
    +bool ShareKeyPersonnelWithModel
    +UpdateDetails(...)
    +UpdateCompliance(...)
    +UpdateCidb(grade, classes)
    +UpdateCapabilities(capabilities)
    +HasValidCsd(asOf) bool
    +HasValidTaxPin(asOf) bool
    +HasValidBbbee(asOf) bool
  }
  class CompanyCapabilities {
    <<JSON value object>>
    +string[] CategoryCodes
    +string[] Technologies
    +string[] Industries
    +CompanyCertification[] Certifications
    +KeyPerson[] KeyPersonnel
    +ReferenceProject[] ReferenceProjects
  }
  class CompanyDocument {
    +Guid CompanyProfileId
    +CompanyDocumentType Type
    +string Title
    +string StoragePath
    +string EncryptedDataKey
    +string Sha256
    +long SizeBytes
    +DateTimeOffset? ExpiresAt
    +DateTimeOffset? DeletedAt
    +IsExpired(asOf) bool
    +ExpiresWithin(window, asOf) bool
    +SoftDelete(now)
    +RewrapDataKey(encryptedDataKey)
  }
  class CompanyDocumentType {
    <<enumeration>>
    CompanyRegistration
    CsdReport
    TaxPin
    BbbeeCertificate
    CompanyProfile
    Reference
    Cv
    Certification
    PricingTemplate
    ProposalTemplate
    Sbd4
    Sbd6Point1
    Other
  }
  CompanyProfile *-- CompanyCapabilities : CapabilitiesJson
  CompanyProfile "1" -- "*" CompanyDocument
  CompanyDocument --> CompanyDocumentType
```

Company documents are stored as `companies/{profileId}/{documentId}.enc`. Each is a TSE1 file with chunked AES-256-GCM
encryption under a random data key. `EncryptedDataKey` is that data key wrapped by the master key, as
`v1|{keyId}|{base64}`. The tax PIN is `[Sensitive]` on requests and masked in responses. Key personnel go to the model by
role only, and only when `ShareKeyPersonnelWithModel` is on.

## Notifications

```mermaid
classDiagram
  direction LR
  class Notification {
    +string DedupeKey
    +NotificationType Type
    +NotificationChannel Channel
    +string Recipient
    +Guid? TenderId
    +Guid? CompanyProfileId
    +string Subject
    +string Body
    +NotificationStatus Status
    +int Attempts
    +DateTimeOffset? NotBefore
    +DateTimeOffset? SentAt
    +string? Error
    +BuildDedupeKey(type, channel, recipient, tenderId, profileId, bucket)$ string
    +Create(...)$ Notification
    +IsDue(now) bool
    +MarkSent(now)
    +MarkFailed(error, now) bool
    +Skip(reason)
    +Defer(until)
  }
  class NotificationPreference {
    +Guid UserId
    +NotificationChannel Channel
    +bool Enabled
    +string Address
    +NotificationType[] Types
    +string? QuietHoursJson
    +Wants(type) bool
  }
  class QuietHours {
    <<value object>>
    +string Start
    +string End
    +string? TimeZone
    +Contains(at) bool
    +EndsAt(at) DateTimeOffset?
  }
  class NotificationDispatcher {
    <<Application service>>
    +DispatchAsync(type, model, tenderId, profileId, bucket, urgent)
  }
  class NotificationSender {
    <<Application service>>
    +SendAsync(notificationId)
    +SendDueAsync()
  }
  class INotificationChannel {
    <<interface>>
    +NotificationChannel Channel
    +SendAsync(message)
  }
  class NotificationType {
    <<enumeration>>
    NewMatch
    QualificationReady
    ClosingSoon7d
    ClosingSoon3d
    ClosingSoon1d
    BriefingReminder
    TenderChanged
    AwardPublished
    PipelineFailure
    DocumentExpiring
    DailyDigest
    Test
  }
  class NotificationChannel {
    <<enumeration>>
    Email
    Telegram
    Teams
    Sms
    WhatsApp
  }
  NotificationPreference *-- "0..1" QuietHours : QuietHoursJson
  NotificationDispatcher ..> NotificationPreference : recipients
  NotificationDispatcher ..> Notification : creates (unique DedupeKey)
  NotificationSender ..> Notification : sends
  NotificationSender ..> INotificationChannel
  Notification --> NotificationType
  Notification --> NotificationChannel
```

A row is `Pending` until it is sent. A failed send retries after 1, 2, 4 and 8 minutes, up to 5 attempts. Channels
that are not configured mark the row `Skipped`. Bid-pack contact details never go into a message (POPIA).

## Operations and security

```mermaid
classDiagram
  direction LR
  class OutboxMessage {
    +string Type
    +string PayloadJson
    +DateTimeOffset OccurredAt
    +DateTimeOffset? ProcessedAt
    +int Attempts
    +string? Error
    +FromEvent(event)$ OutboxMessage
    +MarkProcessed(now)
    +MarkFailed(error) bool
  }
  class AuditLog {
    +Guid? ActorUserId
    +string ActorName
    +string Action
    +string EntityType
    +string? EntityId
    +string? BeforeJson
    +string? AfterJson
    +string? Ip
    +DateTimeOffset At
  }
  class JobRun {
    +string JobName
    +DateTimeOffset StartedAt
    +DateTimeOffset? FinishedAt
    +bool? Success
    +int ItemsProcessed
    +string? DetailsJson
    +Start(jobName, now)$ JobRun
    +Complete(items, details, now)
    +Fail(error, items, now)
  }
  class LlmCall {
    +string Purpose
    +string Provider
    +string Model
    +string? PromptVersion
    +Guid? TenderId
    +int TokensIn
    +int TokensOut
    +decimal CostUsd
    +bool Success
  }
  class ApplicationUser {
    <<Identity>>
    +string? DisplayName
    +DateTimeOffset? LastLoginAt
  }
  class ApplicationRole {
    <<Identity>>
    Admin / Reviewer / Viewer
  }
  class RefreshToken {
    +Guid UserId
    +string TokenHash
    +DateTimeOffset ExpiresAt
    +DateTimeOffset? RevokedAt
    +string? ReplacedByTokenHash
    +IsActive(now) bool
    +Revoke(now, ip, replacedBy)
  }
  class BidApplication {
    <<Phase 2>>
    +Guid TenderId
    +Guid CompanyProfileId
    +ApplicationStatus Status
    +string ChecklistJson
    +Transition(status)
    +RecordSubmission(by, reference, now)
  }
  class ApplicationDocument {
    <<Phase 2>>
    +ApplicationDocumentKind Kind
    +string Title
    +int Version
    +Approve(by, now)
  }
  ApplicationUser "*" -- "*" ApplicationRole : user_roles
  ApplicationUser "1" -- "*" RefreshToken
  ApplicationUser "1" .. "*" NotificationPreference : UserId
  BidApplication "1" *-- "*" ApplicationDocument
  AuditLog ..> ApplicationUser : ActorUserId
```

The tables these types write:

- **`outbox_messages`:** `OutboxMessage`, the hand-off between pipeline stages.
- **`audit_logs`:** `AuditBehaviour` writes a row for every command, with `[Sensitive]` values redacted. Sign-ins and
  company document downloads are recorded too.
- **`job_runs`:** `JobRun`, written for every job run.
- **`llm_calls`:** `LlmCall`, written for every model call, including failed ones. It feeds the daily cost cap and the
  cost chart.
- **`refresh_tokens`:** `RefreshToken` keeps only the token's hash and rotates it on every refresh.

`BidApplication` and `ApplicationDocument` exist for Phase 2. In v1 nothing writes them, and nothing ever submits a bid
(ADR-0015).
