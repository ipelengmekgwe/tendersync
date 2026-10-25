import Link from "next/link";
import { AlertTriangle, ArrowRight, CheckCircle2, XCircle } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { DisclaimerBanner } from "@/components/disclaimer-banner";
import { serverApi } from "@/lib/api/server";
import type { MatchListItem, Overview } from "@/lib/api/types";
import { getSession } from "@/lib/auth/session";
import { ExpiringItemKind, QualificationVerdict, label, verdictTone } from "@/lib/enums";
import { closingIn, formatDate, formatDateTime } from "@/lib/format";

export const metadata = { title: "Overview" };

const usd = new Intl.NumberFormat("en-ZA", { style: "currency", currency: "USD", maximumFractionDigits: 2 });

function MatchList({ items, empty }: { items: MatchListItem[]; empty: string }) {
  if (items.length === 0) {
    return <p className="text-sm text-muted-foreground">{empty}</p>;
  }
  return (
    <ul className="divide-y text-sm">
      {items.map((m) => (
        <li key={m.id} className="space-y-1 py-2 first:pt-0 last:pb-0">
          <Link href={`/tenders/${m.tenderId}`} className="line-clamp-2 font-medium hover:underline">
            {m.title}
          </Link>
          <div className="flex flex-wrap items-center gap-2 text-xs text-muted-foreground">
            <span className="font-mono">{m.bidNumber}</span>
            <span>· closes {formatDateTime(m.closingAt)}</span>
            {closingIn(m.closingAt) ? <span>({closingIn(m.closingAt)})</span> : null}
            {m.verdict !== null ? <Badge variant={verdictTone(m.verdict)}>{label(QualificationVerdict, m.verdict)}</Badge> : null}
          </div>
        </li>
      ))}
    </ul>
  );
}

function PipelineTile({ pipeline }: { pipeline: Overview["pipeline"] }) {
  return (
    <Card>
      <CardHeader>
        <CardTitle className="flex items-center gap-2">
          {pipeline.healthy ? <CheckCircle2 className="size-5 text-emerald-600" aria-hidden /> : <AlertTriangle className="size-5 text-amber-600" aria-hidden />}
          Pipeline {pipeline.healthy ? "healthy" : "needs attention"}
        </CardTitle>
        <CardDescription>
          {pipeline.failedRunsLast24Hours === 0 ? "No failed job runs in the last 24 hours." : `${pipeline.failedRunsLast24Hours} failed job run(s) in the last 24 hours.`}
        </CardDescription>
      </CardHeader>
      <CardContent className="space-y-3 text-sm">
        {pipeline.sources.length > 0 ? (
          <ul className="space-y-1">
            {pipeline.sources.map((s) => (
              <li key={s.source} className="flex items-center gap-2">
                {s.ok ? <CheckCircle2 className="size-4 text-emerald-600" aria-label="OK" /> : <XCircle className="size-4 text-destructive" aria-label="Failing" />}
                <span className="font-medium">{s.source}</span>
                <span className="text-xs text-muted-foreground">checked {formatDateTime(s.checkedAt)}</span>
              </li>
            ))}
          </ul>
        ) : null}
        {pipeline.jobs.length > 0 ? (
          <ul className="space-y-1 text-xs text-muted-foreground">
            {pipeline.jobs.map((j) => (
              <li key={j.jobName}>
                <span className={j.success === false ? "font-medium text-destructive" : "text-foreground"}>{j.jobName}</span> · {formatDateTime(j.startedAt)}
                {j.success === false ? " · failed" : j.success === null ? " · running" : ""}
              </li>
            ))}
          </ul>
        ) : (
          <p className="text-muted-foreground">No job has run yet.</p>
        )}
      </CardContent>
    </Card>
  );
}

/** Overview (PLAN.md §11): new matches, closing soon, pipeline status, model cost this month (Admin) and expiring documents. */
export default async function OverviewPage() {
  const session = await getSession();
  const client = await serverApi();
  const { data, response } = await client.GET("/api/v1/overview");

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-2xl font-semibold tracking-tight">Welcome, {session?.user.displayName}</h1>
        <p className="text-sm text-muted-foreground">Tenders are ingested every 30 minutes; matched ones are extracted and qualified automatically.</p>
      </div>
      <DisclaimerBanner />
      {!data ? (
        <p role="alert" className="rounded-md border border-destructive/40 bg-destructive/10 p-3 text-sm">
          The overview could not be loaded (API responded {response.status}). Refresh the page or sign in afresh.
        </p>
      ) : (
        <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
          <Card>
            <CardHeader>
              <CardTitle className="flex items-baseline justify-between gap-2">
                New matches <span className="text-3xl font-semibold tabular-nums" data-testid="new-match-count">{data.newMatchCount}</span>
              </CardTitle>
              <CardDescription>Waiting for someone to review them.</CardDescription>
            </CardHeader>
            <CardContent className="space-y-3">
              <MatchList items={data.newMatches} empty="No new matches." />
              <Link href="/matches" className="inline-flex items-center gap-1 text-sm font-medium text-primary hover:underline">
                Open the inbox <ArrowRight className="size-4" aria-hidden />
              </Link>
            </CardContent>
          </Card>

          <Card>
            <CardHeader>
              <CardTitle>Closing soon</CardTitle>
              <CardDescription>Matches still in play that close within {data.closingSoonDays} days.</CardDescription>
            </CardHeader>
            <CardContent className="space-y-3">
              <MatchList items={data.closingSoon} empty={`Nothing closes in the next ${data.closingSoonDays} days.`} />
              <Link href="/matches?soon=true" className="inline-flex items-center gap-1 text-sm font-medium text-primary hover:underline">
                All closing soon <ArrowRight className="size-4" aria-hidden />
              </Link>
            </CardContent>
          </Card>

          <PipelineTile pipeline={data.pipeline} />

          {data.cost ? (
            <Card>
              <CardHeader>
                <CardTitle>Model cost this month</CardTitle>
                <CardDescription>Since {formatDate(data.cost.monthStart)} · {data.cost.calls} call(s)</CardDescription>
              </CardHeader>
              <CardContent className="space-y-1 text-sm">
                <div className="text-3xl font-semibold tabular-nums">{usd.format(data.cost.costUsd)}</div>
                <div className="text-muted-foreground">
                  Today {usd.format(data.cost.spentTodayUsd)} of the {usd.format(data.cost.dailyCapUsd)} daily cap
                </div>
              </CardContent>
            </Card>
          ) : null}

          <Card>
            <CardHeader>
              <CardTitle>Documents expiring</CardTitle>
              <CardDescription>Company documents and registrations expired or expiring within {data.expiryWindowDays} days.</CardDescription>
            </CardHeader>
            <CardContent>
              {data.expiring.length === 0 ? (
                <p className="text-sm text-muted-foreground">Nothing expiring.</p>
              ) : (
                <ul className="space-y-2 text-sm">
                  {data.expiring.map((e, i) => (
                    <li key={`${e.kind}-${e.documentId ?? e.companyProfileId}-${i}`} className="flex flex-wrap items-center justify-between gap-2">
                      <span>
                        <span className="font-medium">{e.name}</span>{" "}
                        <span className="text-xs text-muted-foreground">({label(ExpiringItemKind, e.kind)})</span>
                      </span>
                      <Badge variant={e.expired ? "destructive" : "secondary"}>
                        {e.expired ? "Expired" : "Expires"} {formatDate(e.expiresAt)}
                      </Badge>
                    </li>
                  ))}
                </ul>
              )}
            </CardContent>
          </Card>
        </div>
      )}
    </div>
  );
}
