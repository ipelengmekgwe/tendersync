# Compliance

What TenderSync takes from public sources, what it keeps about people and companies, and the rules the code follows
because of it. PLAN.md §2 and §12 hold the verified source facts and the security design; this page records how they
are met. It is not legal advice.

## Data source: National Treasury eTenders

| Item | Fact | Source |
|---|---|---|
| API | `https://ocds-api.etenders.gov.za/api/OCDSReleases` (OCDS 1.1 release packages) | PLAN.md §2, `docs/research/` |
| Licence | Open Data Commons Public Domain Dedication and License (PDDL) 1.0, stated in every release package's `license` field | `docs/research/etenders-samples/` |
| Publication policy | Any use, including commercial. Republishing should link back to the source. The data is a public beta, and Treasury does not verify it; the policy says it "must not be used for any critical decision making or legal purposes" | PLAN.md §2 (verified 23 Oct 2026) |
| Personal information | The policy excludes personal information unless consented (POPIA) | PLAN.md §2 |

### How TenderSync follows it

- **A source link on every tender.**
  - `Tender.SourceUrl` points at `https://www.etenders.gov.za/Home/TenderDetails?Id=…`.
  - The dashboard's tender pages, every notification template and the qualification report all link to it.
  - The original bid documents are always one click away: they are stored unchanged and downloadable from the tender
    page.
- **A disclaimer wherever tender data is shown.**
  - **Dashboard:** `web/src/components/disclaimer-banner.tsx` appears on the overview, matches, tenders, tender detail
    and document viewer pages.
  - **Qualification report:** `QualificationReportRenderer.Disclaimer` opens every report.
  - **Notifications:** every email, Telegram and Teams template ends with "Verify against the source documents before
    acting". The Telegram and Teams lines were added in WP18.
- **Extracted claims point at their evidence.** Every extracted requirement carries its source document, page and quote
  where the model could give one. `NeedsVerification` flags low confidence and missing citations. Model output never
  decides a hard fact on its own (PLAN.md §7.5): registrations, grades and thresholds are checked by deterministic rules
  in `TenderSync.Domain/Qualification`.
- **No automated submission.** The system never submits a bid or signs in to the portal (ADR-0015). It stops at a
  report and instructions for a person.
- **Polite access.** Requests go out with the user agent `TenderSync/1.0 (+https://github.com/ipelengmekgwe/tendersync)`.
  - The feed is read in pages of 100, the size the API supports, over a 3-day window every 30 minutes, plus a weekly
    90-day backfill in 7-day slices.
  - Document downloads come only from `www.etenders.gov.za`, at most 2 at a time and 500 ms apart.
  - Retries back off, and a circuit breaker stops requests while the API is failing.
- **Attribution.** None is required under the PDDL, but the README and the dashboard name the source.

## Personal information (POPIA)

| Data | Where it comes from | Where it is kept | Who can see it | Sent elsewhere? |
|---|---|---|---|---|
| User accounts: name, email, password hash, roles, sign-in times | The admin creates them | `users`, `refresh_tokens` (hashed tokens only) | Admins | No |
| Notification addresses: email, Telegram chat id, Teams webhook, phone | Each user | `notification_preferences` (`[Sensitive]` on requests, masked in listings and logs) | The user; recipients are masked in notification listings and logs | Only to that channel's provider when a message is sent |
| Buyer officials' enquiry contacts from bid packs | Requirement extraction (`ContactsV1`) | One requirement row, "Enquiries contact (internal use only)" | Signed-in users | **Never**: notifications, exports and reports leave them out |
| Company key personnel (name, role, experience, summary) | Company profile | `company_profiles.capabilities_json` | Reviewers and Admins | To the model **by role only**, and only when `ShareKeyPersonnelWithModel` is on |
| Company documents (registration, tax, B-BBEE, CVs…) | Uploaded by Admins | Encrypted (AES-256-GCM per file, master key in Key Vault or the environment) | Reviewers and Admins; every download is audited | Never sent to a model or a channel |
| Audit trail: actor, action, redacted request, IP | Every command | `audit_logs` | Admins | No |

Rules the code enforces:

- **Requests:** properties that hold secrets or personal contact data are marked `[Sensitive]`. `RequestRedactor` replaces
  them in logs and audit rows, including in nested objects.
- **Notifications** are built from `NotificationModel`, which has no field for bid-pack contacts. The template snapshots
  in `tests/TenderSync.Application.Tests/Notifications/Snapshots` would show one appearing.
- **The tax PIN** is never returned by the API; responses carry it masked.
- **Data residency:** the Azure templates deploy into the resource group's region. The runbook recommends South Africa
  North. Local and VPS deployments keep everything on the host.

## Model providers

These are sent to the configured provider (Anthropic by default, ADR-0012):

- Public tender text and bid documents.
- Company profile facts needed to judge a requirement: registrations, grades, turnover, years trading, certifications,
  reference projects, and key personnel roles when allowed.

Company documents are not sent. Every call is recorded in `llm_calls` with its purpose, model, tokens and cost, and
`Llm:DailyCostCapUsd` caps the spend. Check the provider's data-retention terms before you route production traffic to
an OpenAI-compatible endpoint you do not control.

## Retention

v1 deletes nothing on a schedule. Raw releases, document files and texts, reports, notifications, audit rows and model
call records are all kept, so history and audit stay complete. Company documents are soft-deleted (`deleted_at`), and
the encrypted file stays in the store. A retention policy (for example, purge notifications and model call records after
a year and hard-delete soft-deleted company documents after 90 days) is open work; see
`docs/security-review.md`.
