# Security review (WP18)

**Date:** 25 October 2026.
**Scope:** the whole repository at the end of Phase 1: the API, Worker, dashboard, Docker Compose, the Azure Bicep
templates and the GitHub workflows.

**Method:**

- A read-only review against the checklist below, with file and line evidence for every result.
- Each finding was checked in the code before triage.
- Dependency vulnerabilities are covered separately by the CI gates (`dotnet list package --vulnerable`,
  `npm audit --omit=dev --audit-level=high`). Both were clean at the time of review.

**Result:** no High findings and four Medium findings, all fixed in WP18. Of the 12 Low and Info findings, three are
fixed and the rest are accepted with a reason or left as follow-up work.

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
| F5 | Low | `GET /notifications` lists messages sent to any address a user has put in their own preferences. A Viewer could enter an Admin's email and read that Admin's notifications. | **Follow-up.** Store the recipient's user id on `notifications` and filter on it, or verify addresses before a preference becomes active. Risk is limited because accounts are created by an Admin for staff of one company (ADR-0016). |
| F6 | Low | Teams webhook URLs are only required to be https, so the Worker will POST to any host, including internal ones (semi-blind SSRF). Test messages go to any address. | **Follow-up**, together with F5: an allowlist of Teams and Power Automate hosts, no stored response bodies, and a rate limit on `/notifications/test`. |
| F7 | Low | The dashboard sets no security headers (CSP, frame-ancestors, nosniff, Referrer-Policy, HSTS). | **Follow-up.** It needs testing against react-pdf's worker and Next's inline scripts, so it ships as its own change with the e2e suite run. |
| F8 | Low | `/api/auth/login` accepts a cross-site `text/plain` form (login CSRF), and `/api/auth/logout` can be triggered cross-site. | **Follow-up.** Require `content-type: application/json` and a matching `Origin` on state-changing `/api/auth/*` and `/api/proxy/*` requests. The proxy itself is protected by SameSite=Lax cookies. |
| F9 | Low | The Hangfire dashboard cookie is set without `Secure` behind TLS termination, and the admin token travels in the query string once. | **Accepted for now.** The dashboard is not exposed on Azure by default (`exposeWorkerDashboard = false`) and is bound to 127.0.0.1 in Compose. **Follow-up** before exposing it: `Secure` always, and a one-time code instead of the token in the URL. |
| F10 | Low | `RequestRedactor` serialised objects nested deeper than its walk in full, including any `[Sensitive]` property inside them. | **Fixed.** It walks 6 levels and replaces anything deeper with `***`. Test: `Objects_nested_deeper_than_the_walk_are_replaced_rather_than_serialised_in_full`. |
| F11 | Low | The document downloader follows redirects without checking each hop against the host allowlist. | **Follow-up.** Turn off automatic redirects, follow them by hand with the allowlist, and reject IP literals and private ranges. Today the first URL must be on an eTenders host, and the metadata endpoints need headers the downloader never sends. |
| F12 | Low | The tender id from the feed went into `SourceUrl` unencoded, and link values were not escaped in email and Telegram HTML. | **Fixed.** `Uri.EscapeDataString` in `ReleaseMapper`, and `html.escape` on every `href` value in the email and Telegram templates. The snapshots are unchanged because real ids are numeric. |
| F13 | Low | PostgreSQL on Azure accepts connections from any Azure IP (`0.0.0.0` rule), and the apps sign in as the server admin. | **Accepted for v1**, as recorded in `docs/runbooks/azure.md` under "Hardening later": the password is 40 random characters and TLS is checked. **Follow-up:** VNet integration or Entra authentication with the managed identity, and a separate least-privilege app role. |
| F14 | Info | `Llm:AllowCompanyDocuments` exists but nothing reads it. | **Follow-up:** remove it or enforce it in the LLM router. Company document files are never sent to a model; `docs/compliance.md` lists exactly what is sent. |
| F15 | Info | The anonymous `/health/ready` endpoints returned exception messages (hosts, addresses). | **Fixed.** Error text is returned only in Development. |
| F16 | Info | Two refreshes at the same moment trip refresh-token reuse detection and sign the user out on every device. | **Follow-up:** a short grace window for the replaced token, or a single in-flight refresh in the proxy. |

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

- **`TenderMatch.MarkNotified` is never called**, so `notified_at` is always empty. Notifications are deduplicated by
  their own key, so nothing is sent twice. The column is simply unused.
- **A document that fails after the others have been extracted can stall extraction.** `ExtractWhenDocumentsAreReady`
  waits until no document is Pending or Downloaded, and a failed or skipped download raises no event, so the last check
  can be missed. `POST /tenders/{id}/extract` recovers it. The fix is to raise an event (or request extraction) when a
  download fails and the rest are done.
- **Ingest and `ComputeStatusJob` use different closing-soon windows.** Ingest uses a fixed 7 days, `ComputeStatusJob`
  uses `Status:ClosingSoonDays`. They agree while that setting is 7.

## Re-running the review

1. Work through the checklist above against the current code.
2. Run `dotnet list TenderSync.slnx package --vulnerable --include-transitive` and
   `cd web && npm audit --omit=dev --audit-level=high`.
3. Record findings in this file with a decision for each.

Fixed findings keep their row, so the history stays readable.
