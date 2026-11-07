# Security review (WP18)

**Date:** 25 October 2026.
**Scope:** the whole repository at the end of Phase 1: the API, Worker, dashboard, Docker Compose, the Azure Bicep
templates and the GitHub workflows.

**Method:**

- A read-only review against the checklist below, with file and line evidence for every result.
- Each finding was checked in the code before triage.
- Dependency vulnerabilities are covered separately by the CI gates (`dotnet list package --vulnerable`,
  `npm audit --omit=dev --audit-level=high`). Both were clean at the time of review.

**Result:** no High findings and four Medium findings, all fixed in WP18. Of the 12 Low and Info findings, three were
fixed in WP18 and seven on the follow-up branch (`fix/review-followups`). F9 is partly fixed, F13 is accepted for v1,
and F14 is kept by decision.

## Checklist

| Area | Result |
|---|---|
| Authentication | Pass. JWTs are validated for issuer, audience, lifetime (30 s skew) and an HS256 key of at least 32 characters. Refresh tokens are 48 random bytes stored as SHA-256 hashes, rotated on every use, and reuse revokes the whole family. Passwords need 12 characters; 5 failures lock the account for 15 minutes. `DevAuth` is forced off outside Development. |
| Authorization | Pass, except F5. All 49 request types carry `[Authorize]`, `[Authorize(role)]` or `[AllowAnonymous]`, and a request without one fails closed. Every endpoint group has a policy, and the Hangfire dashboard needs Admin. Company and tender document access checks the owning id. |
| Dashboard | Pass, except F2, F7 and F8. Cookies are httpOnly, SameSite=Lax and Secure in production. There is no `dangerouslySetInnerHTML` and the PDF highlighter escapes its input. |
| API hardening | Pass, except F1 and F15. CORS has an explicit origin list, HSTS is on outside Development, 500 responses are generic, and uploads are capped while they stream. OpenAPI and Scalar stay mapped: the API is internal on Azure and bound to 127.0.0.1 in Compose. |
| SSRF and files | Pass, except F6 and F11. Downloads are limited to the eTenders hosts, http(s), 50 MB and a timeout. Zip archives are limited to depth 2, 500 entries and 200 MB. The filesystem store rejects rooted paths, `..` and paths that escape its root. The stored extension comes from magic bytes. |
| Secrets and logging | Pass, except F3, F9 and F10. Passwords, tokens, the tax PIN, preference addresses and upload streams are `[Sensitive]`. Recipients are masked in listings. No secrets are in tracked files. |
| Encryption | Pass. Each file gets a random 256-bit data key. Nonces are an 8-byte random prefix plus a 32-bit counter. The chunk index and a final-chunk flag are in the AAD, so reordering and truncation are detected. Plaintext is released only after its tag verifies. Key wrapping uses AES-GCM with the key id as AAD (environment provider) or RSA-OAEP-256 with a wrap/unwrap-only key (Key Vault). |
| Model use | Pass, except F4 and F14. Answers are structured records. Hard facts are decided by rules. The daily cost cap is checked before every call, and failed calls are still billed. Key personnel are shared only with consent. |
| Templates | Pass, except F12. Every free-text value is `html.escape`d in email and Telegram. The notification model has no contact fields (POPIA). |
| Infrastructure | Pass, except F13. Compose binds to 127.0.0.1 and containers run as non-root. Key Vault uses RBAC, storage has shared keys off, the API ingress is internal, and the workflows have least-privilege permissions, OIDC, masked secrets and no `pull_request_target`. |

## Findings and triage

| ID | Severity | Finding | Decision |
|---|---|---|---|
| F1 | Medium | The sign-in rate limit counted every user as the dashboard container (all API calls come from it). Ten bad logins a minute blocked sign-in for everyone, and busy refreshing could trip it too. | **Fixed.** The dashboard passes the browser's `X-Forwarded-For` to the API. The API trusts forwarded hops only from private networks and walks to the first public address. Login keeps 10 per minute per client; refresh, logout and `/me` get their own 60 per minute. Tests: `ForwardedClientRateLimitTests`, `forwarded.test.ts`. |
| F2 | Medium | Open redirect: `/login?next=https://evil.example` (signed in) or `next=//evil.example` (after sign-in) left the site. | **Fixed.** `safeNextPath` (`web/src/lib/auth/redirect.ts`) accepts only same-site paths, and the middleware and login page use it. Test: `redirect.test.ts`. |
| F3 | Medium | The Telegram bot token is part of the URL path. HttpClientFactory logs "Start processing HTTP request POST {uri}" at Information, so the token reached the console, Seq and Application Insights. | **Fixed.** Three changes: the Telegram, Teams and SMS clients have their request logging removed (`RemoveAllLoggers`); `System.Net.Http.HttpClient` logs at Warning in both hosts; and outgoing-call telemetry records those URLs redacted (`SecretUrlRedactor`). Test: `SecretUrlRedactorTests`. |
| F4 | Medium | A fuzzy certification match from the model turned a mandatory requirement into Pass without checking the company held that certification. Wording planted in a bid document, or a hallucination, could raise the verdict. | **Fixed.** The match counts only when it names a certification on the profile, and that certification must still be valid when the bid closes (otherwise Expired). Anything else stays Unknown with the model's text as a hint. Test: `A_model_match_only_counts_for_a_certification_the_profile_holds_and_that_is_valid_at_closing`. |
| F5 | Low | `GET /notifications` lists messages sent to any address a user has put in their own preferences. A Viewer could enter an Admin's email and read that Admin's notifications. | **Fixed (follow-up branch).** Each notification records the user whose preference created it (`recipient_user_id`), and `GET /notifications` filters on that, never on the address. The migration gives existing rows to the one user whose preference holds that channel and address; rows several users claim stay unassigned. Test: the claimed-address case in `A_test_message_is_queued_for_the_callers_channel_and_listed_with_a_masked_recipient`. |
| F6 | Low | Teams webhook URLs are only required to be https, so the Worker will POST to any host, including internal ones (semi-blind SSRF). Test messages go to any address. | **Fixed (follow-up branch).** Teams addresses must be https on the default port at `*.webhook.office.com`, `*.logic.azure.com` or `*.powerplatform.com` (`TeamsWebhook`), checked when a preference is saved and again before every send. The remote response body is no longer logged or stored. `POST /notifications/test` is limited to 5 per minute per client (`RateLimiting:TestSend`). Test: `Teams_never_posts_to_a_host_outside_the_webhook_allowlist`. |
| F7 | Low | The dashboard sets no security headers (CSP, frame-ancestors, nosniff, Referrer-Policy, HSTS). | **Fixed (follow-up branch)**, without script restrictions. Every response carries a CSP with `frame-ancestors 'none'`, `base-uri 'self'`, `form-action 'self'` and `object-src 'none'`, plus X-Frame-Options, nosniff, Referrer-Policy and Permissions-Policy (`web/next.config.ts`). The middleware adds HSTS when the request came over HTTPS. A `script-src` policy needs nonces for Next's inline scripts and the pdf.js worker; it remains follow-up work. |
| F8 | Low | `/api/auth/login` accepts a cross-site `text/plain` form (login CSRF), and `/api/auth/logout` can be triggered cross-site. | **Fixed (follow-up branch).** The middleware refuses state-changing `/api/*` requests that `Sec-Fetch-Site` (or, failing that, `Origin`) marks as cross-site, with 403 (`web/src/lib/auth/csrf.ts`). `/api/auth/login` also requires a JSON body (415 otherwise), which an HTML form cannot send. Test: `csrf.test.ts`. |
| F9 | Low | The Hangfire dashboard cookie is set without `Secure` behind TLS termination, and the admin token travels in the query string once. | **Partly fixed (follow-up branch).** The cookie is `Secure` everywhere except plain-http loopback, so it keeps the flag behind the Azure ingress. The Worker no longer logs request URLs, which carried the token, at Information in Development. **Still open** before exposing the dashboard: a one-time code instead of the token in the URL. |
| F10 | Low | `RequestRedactor` serialised objects nested deeper than its walk in full, including any `[Sensitive]` property inside them. | **Fixed.** It walks 6 levels and replaces anything deeper with `***`. Test: `Objects_nested_deeper_than_the_walk_are_replaced_rather_than_serialised_in_full`. |
| F11 | Low | The document downloader follows redirects without checking each hop against the host allowlist. | **Fixed (follow-up branch).** The download client no longer follows redirects itself. `HttpDocumentDownloader` follows up to 5 and checks every hop with `DocumentOptions.AllowsDownloadFrom`. With an empty allowlist, IP literals and localhost are still refused. Tests: `A_redirect_off_the_allowed_hosts_is_refused_before_it_is_requested`, `Without_an_allowlist_ip_literals_and_localhost_are_still_refused`. |
| F12 | Low | The tender id from the feed went into `SourceUrl` unencoded, and link values were not escaped in email and Telegram HTML. | **Fixed.** `Uri.EscapeDataString` in `ReleaseMapper`, and `html.escape` on every `href` value in the email and Telegram templates. The snapshots are unchanged because real ids are numeric. |
| F13 | Low | PostgreSQL on Azure accepts connections from any Azure IP (`0.0.0.0` rule), and the apps sign in as the server admin. | **Accepted for v1**, as recorded in `docs/runbooks/azure.md` under "Hardening later": the password is 40 random characters and TLS is checked. **Follow-up:** VNet integration or Entra authentication with the managed identity, and a separate least-privilege app role. |
| F14 | Info | `Llm:AllowCompanyDocuments` exists but nothing reads it. | **Kept by decision.** PLAN.md §8 makes the switch a rule: company documents reach a provider only when it is on. No v1 feature sends them (`docs/compliance.md` lists what is sent), so nothing reads it yet. The first feature that does (Phase 2 proposal generation) must check it; the option's comment now says so. |
| F15 | Info | The anonymous `/health/ready` endpoints returned exception messages (hosts, addresses). | **Fixed.** Error text is returned only in Development. |
| F16 | Info | Two refreshes at the same moment trip refresh-token reuse detection and sign the user out on every device. | **Fixed (follow-up branch).** For 30 seconds after a rotation, a second use of the old token answers 409 and leaves the family alone; later reuse still revokes everything. The dashboard proxy turns that 409 into a retryable 503 and keeps the cookies the other request set. Test: `Refresh_rotates_the_token_and_reuse_of_the_old_one_is_rejected`. |

**Also noted:**

- The runtime image is `aspnet:10.0` rather than the chiseled image PLAN.md §12 suggests. Moving needs the bash
  healthcheck replaced.
- Images and Actions are pinned by tag or major version, not digest. Dependabot keeps them current.
- The zip budget counts declared entry sizes. Recent .NET stops inflating at the declared length, but this was not
  confirmed by test.
- Nothing is deleted on a schedule, so a retention policy is open work (`docs/compliance.md`, Retention).

## Pipeline issues found while drawing the diagrams

These are correctness rather than security issues. They were found while tracing the flows for `docs/diagrams` and are
recorded here so they are not lost.

- **`TenderMatch.MarkNotified` was never called**, so `notified_at` was always empty. **Fixed:** `NotifyOnNewMatch`
  stamps the match once a new-match message is created or already exists.
- **A document that failed after the others had text could stall extraction.** **Fixed:** `TenderDocument.MarkFailed`
  and `MarkSkipped` raise `DocumentNotExtracted`, and `ExtractWhenDocumentsAreReady` runs the same readiness check for
  it. It also waits until at least one document has text. Tests: `ExtractionTriggerTests`.
- **Ingest and `ComputeStatusJob` used different closing-soon windows.** **Fixed:** `TenderIngestor` reads
  `Status:ClosingSoonDays` too.

## Re-running the review

1. Work through the checklist above against the current code.
2. Run `dotnet list TenderSync.slnx package --vulnerable --include-transitive` and
   `cd web && npm audit --omit=dev --audit-level=high`.
3. Record findings in this file with a decision for each.

Fixed findings keep their row, so the history stays readable.
