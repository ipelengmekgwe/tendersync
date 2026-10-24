# Extraction golden set

Twenty hand-labelled bid packs for the WP08 acceptance check (PLAN.md §7.4): field-level accuracy ≥ 80% on the key fields.

## How to build the templates

1. Pick 20 matched tenders whose documents are downloaded and extracted (`GET /api/v1/tenders?matchedOnly=true`, or any ocids).
2. Run the extractor once with a model key and let it write a template per tender:

```bash
dotnet run --project src/TenderSync.Worker -- --extract ocds-9t57fa-175186,ocds-9t57fa-175322 --extraction-template docs/golden/extraction
```

Each `{ocid}.json` has two blocks: `extracted` (what the model stored, for reference) and `labels` (yours).

## How to label

Open the bid document and fill `labels`:

| Field | Meaning |
|---|---|
| `closingAt` | Closing date and time as printed, ISO 8601 with `+02:00` (`2026-11-20T11:00:00+02:00`); `null` if the pack has none. |
| `briefingAt`, `briefingCompulsory` | Briefing or site meeting; `null` when there is none. |
| `functionalityThreshold` | Minimum functionality score out of 100 (`70`), or `null`. |
| `evaluationMethod` | `80/20`, `90/10`, `functionality then price`, or `null`. |
| `mandatoryTypes` | Types of the pass/fail requirements present: `CsdRegistration`, `TaxCompliance`, `BbbeeCertificate`, `CidbGrade`, `CompanyRegistration`, `Insurance`, `YearsInBusiness`, `MinimumTurnover`, `References`, `Certification`, `Accreditation`, `LocalContent`, `JvSubcontractingRule`, `Other`. |
| `requiredFormCodes` | Form codes the pack requires: `SBD1`, `SBD4`, `SBD6.1`, `MBD4`, … (spacing and case do not matter). |

A `null` scalar label is not scored; an empty list means "none in the pack" and is scored. Add a `note` when a judgement call was needed.

## How to run the eval

```bash
TENDERSYNC_EVALS=1 TENDERSYNC_CONNECTION="Host=localhost;Port=5432;Database=tendersync;Username=tendersync;Password=tendersync" dotnet test tests/TenderSync.Evals --filter FullyQualifiedName~ExtractionEvalTests
```

It compares the labels with what is stored in the database for each tender (closing date from the submission row or the feed, briefing columns on the tender, the evaluation row, mandatory types and required form codes) and reports per-tender and average accuracy.
