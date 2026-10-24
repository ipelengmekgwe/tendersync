# Notification templates

One Scriban file per notification family and channel: `{family}.{channel}.sbn` (PLAN.md §9). The first line of the
rendered text is the subject, the rest is the body. `NotificationTemplates` falls back from the exact family to
`default.{channel}.sbn` and finally `default.text.sbn`, which also serves SMS and WhatsApp.

| Channel | Body format | Notes |
|---|---|---|
| `email` | HTML | A plain-text alternative is derived by stripping tags. Escape values with `html.escape`. |
| `telegram` | Telegram HTML subset (`b`, `i`, `a`, `code`, `pre`) | Escape values with `html.escape`; the channel adds the inline "Open tender" button. |
| `teams` | Markdown subset supported by Adaptive Card TextBlocks | The channel builds the card and the OpenUrl action. |
| `text` | Plain text | SMS and WhatsApp; keep it under 320 characters. |

Families: new-match, qualification-ready, closing-soon (ClosingSoon7d/3d/1d), briefing-reminder, tender-changed,
award-published, document-expiring, pipeline-failure, daily-digest, test.

Members are the C# property names of `NotificationModel` (`BidNumber`, `Title`, `Buyer`, `Province`, `Closing`, `ClosingIn`,
`Relevance`, `Categories`, `Summary`, `Verdict`, `Score`, `EstimatedFunctionality`, `MissingItems`, `QuestionCount`,
`TenderUrl`, `SourceUrl`, `Company`, `Briefing`, `Change`, `Source`, `Message`, `DocumentTitle`, `ExpiresOn`, `DaysLeft`,
`NewMatches`, `ClosingSoon`, `Qualified`, `Date`, `DashboardUrl`). A changed template must keep its snapshot test in
`tests/TenderSync.Application.Tests/Notifications/Snapshots` green or have the snapshot accepted deliberately.
