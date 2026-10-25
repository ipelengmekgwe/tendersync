import { Badge } from "@/components/ui/badge";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import type { QualificationReport } from "@/lib/api/types";
import { FindingOutcome, QualificationVerdict, RequirementType, findingTone, label, verdictTone } from "@/lib/enums";
import { formatDateTime } from "@/lib/format";

/** "Can we bid?" (PLAN.md §11): verdict, score, missing items, estimated functionality against the threshold, questions. */
export function QualificationCard({ report, functionalityThreshold }: { report: QualificationReport | null; functionalityThreshold: number | null }) {
  if (!report) {
    return (
      <Card id="can-we-bid">
        <CardHeader>
          <CardTitle>Can we bid?</CardTitle>
          <CardDescription>No qualification report yet. It runs after requirement extraction; a reviewer can queue it from the actions above.</CardDescription>
        </CardHeader>
      </Card>
    );
  }

  const estimate = report.estimatedFunctionalityScore;
  const belowThreshold = estimate !== null && functionalityThreshold !== null && estimate < functionalityThreshold;

  return (
    <Card id="can-we-bid">
      <CardHeader>
        <CardTitle className="flex flex-wrap items-center gap-2">
          Can we bid?
          <Badge variant={verdictTone(report.verdict)} data-testid="verdict">
            {label(QualificationVerdict, report.verdict)}
          </Badge>
        </CardTitle>
        <CardDescription>
          For {report.companyName} · score {report.score}/100 · {formatDateTime(report.createdAt)} · {report.model} ({report.promptVersion})
        </CardDescription>
      </CardHeader>
      <CardContent className="space-y-5 text-sm">
        <dl className="grid gap-3 sm:grid-cols-3">
          <div className="rounded-md border p-3">
            <dt className="text-xs text-muted-foreground">Score</dt>
            <dd className="text-2xl font-semibold tabular-nums">{report.score}</dd>
          </div>
          <div className="rounded-md border p-3">
            <dt className="text-xs text-muted-foreground">Estimated functionality</dt>
            <dd className={belowThreshold ? "text-2xl font-semibold text-destructive tabular-nums" : "text-2xl font-semibold tabular-nums"}>
              {estimate ?? "—"}
              {functionalityThreshold !== null ? <span className="text-sm font-normal text-muted-foreground"> / threshold {functionalityThreshold}</span> : null}
            </dd>
          </div>
          <div className="rounded-md border p-3">
            <dt className="text-xs text-muted-foreground">Missing</dt>
            <dd className="text-2xl font-semibold tabular-nums">{report.missingMandatory.length + report.missingDocuments.length}</dd>
          </div>
        </dl>

        {report.missingMandatory.length > 0 || report.missingDocuments.length > 0 ? (
          <section className="space-y-2">
            <h2 className="font-medium">Missing</h2>
            <ul className="list-disc space-y-1 pl-5">
              {[...report.missingMandatory, ...report.missingDocuments].map((item, i) => (
                <li key={`${item.name}-${i}`}>
                  <span className="font-medium">{item.name}</span>
                  {item.formCode ? <span className="text-muted-foreground"> ({item.formCode})</span> : null}: {item.reason}
                </li>
              ))}
            </ul>
          </section>
        ) : null}

        {report.findings.length > 0 ? (
          <section className="space-y-2">
            <h2 className="font-medium">Checks against the profile</h2>
            <ul className="space-y-2">
              {report.findings.map((f, i) => (
                <li key={`${f.requirement}-${i}`} className="flex flex-col gap-1 sm:flex-row sm:items-start sm:gap-3">
                  <Badge variant={findingTone(f.outcome)} className="w-fit shrink-0">
                    {label(FindingOutcome, f.outcome)}
                  </Badge>
                  <div>
                    <div>
                      {f.requirement} <span className="text-xs text-muted-foreground">({label(RequirementType, f.type)})</span>
                    </div>
                    <div className="text-xs text-muted-foreground">{f.detail}</div>
                  </div>
                </li>
              ))}
            </ul>
          </section>
        ) : null}

        {report.functionalityBreakdown.length > 0 ? (
          <section className="space-y-2">
            <h2 className="font-medium">Functionality estimate</h2>
            <div className="overflow-x-auto">
              <table className="w-full text-left text-sm">
                <thead className="text-xs text-muted-foreground">
                  <tr>
                    <th className="py-1 pr-3 font-medium">Criterion</th>
                    <th className="py-1 pr-3 font-medium tabular-nums">Weight</th>
                    <th className="py-1 pr-3 font-medium tabular-nums">Estimate</th>
                    <th className="py-1 font-medium">Why</th>
                  </tr>
                </thead>
                <tbody>
                  {report.functionalityBreakdown.map((s) => (
                    <tr key={s.criterion} className="border-t align-top">
                      <td className="py-1.5 pr-3">{s.criterion}</td>
                      <td className="py-1.5 pr-3 tabular-nums">{s.weight}</td>
                      <td className="py-1.5 pr-3 tabular-nums">{s.estimated}</td>
                      <td className="py-1.5 text-muted-foreground">{s.reasoning}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </section>
        ) : null}

        {report.questions.length > 0 ? (
          <section className="space-y-2">
            <h2 className="font-medium">Questions for a person</h2>
            <ol className="list-decimal space-y-1 pl-5">
              {report.questions.map((q, i) => (
                <li key={i}>
                  {q.question} <span className="text-xs text-muted-foreground">{q.reason}</span>
                </li>
              ))}
            </ol>
          </section>
        ) : null}

        <details className="rounded-md border p-3">
          <summary className="cursor-pointer text-sm font-medium">Full report</summary>
          <pre className="mt-2 font-sans text-sm whitespace-pre-wrap">{report.summaryMarkdown}</pre>
        </details>
      </CardContent>
    </Card>
  );
}
