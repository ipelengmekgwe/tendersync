"use client";

import { useState } from "react";
import { keepPreviousData, useMutation, useQuery } from "@tanstack/react-query";
import { AlertTriangle, CheckCircle2, Play, XCircle } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Label } from "@/components/ui/label";
import { Skeleton } from "@/components/ui/skeleton";
import { CostChart, fillDays } from "@/components/admin/cost-chart";
import { api } from "@/lib/api/client";
import { unwrap } from "@/lib/api/problem";
import type { JobRun, SourceHealth } from "@/lib/api/types";
import { formatDateTime } from "@/lib/format";

const usd = new Intl.NumberFormat("en-ZA", { style: "currency", currency: "USD", minimumFractionDigits: 2, maximumFractionDigits: 2 });
const RUNS_PAGE = 25;

export function runDuration(run: Pick<JobRun, "startedAt" | "finishedAt">): string {
  if (!run.finishedAt) {
    return "running";
  }
  const seconds = Math.max(0, Math.round((new Date(run.finishedAt).getTime() - new Date(run.startedAt).getTime()) / 1000));
  return seconds < 60 ? `${seconds}s` : `${Math.floor(seconds / 60)}m ${seconds % 60}s`;
}

/** Newest probe per source, plus whether any recent probe reported schema drift. */
export function latestPerSource(checks: SourceHealth[]): (SourceHealth & { drift: boolean })[] {
  const latest = new Map<string, SourceHealth & { drift: boolean }>();
  for (const check of [...checks].sort((a, b) => b.checkedAt.localeCompare(a.checkedAt))) {
    if (!latest.has(check.source)) {
      latest.set(check.source, { ...check, drift: false });
    }
    const entry = latest.get(check.source)!;
    if (check.schemaDriftJson && check.schemaDriftJson !== "[]" && check.schemaDriftJson !== "null") {
      entry.drift = true;
    }
  }
  return [...latest.values()].sort((a, b) => a.source.localeCompare(b.source));
}

function StateBadge({ success }: { success: boolean | null | undefined }) {
  if (success === null || success === undefined) {
    return <Badge variant="outline">Running</Badge>;
  }
  return success ? (
    <Badge variant="secondary" className="gap-1">
      <CheckCircle2 className="size-3" aria-hidden /> Succeeded
    </Badge>
  ) : (
    <Badge variant="destructive" className="gap-1">
      <XCircle className="size-3" aria-hidden /> Failed
    </Badge>
  );
}

/**
 * /admin/pipeline (PLAN.md §11): recurring jobs with "run now", job runs, source health with drift, and model cost.
 * "Run now" goes through the outbox, so the Worker starts the job within about a minute; the run then shows below.
 */
export function PipelineDashboard() {
  const [jobFilter, setJobFilter] = useState("");
  const [page, setPage] = useState(1);
  const [requested, setRequested] = useState<Record<string, string>>({});

  const jobs = useQuery({ queryKey: ["admin", "jobs"], queryFn: async () => unwrap(await api.GET("/api/v1/admin/jobs")), refetchInterval: 60_000 });
  const runs = useQuery({
    queryKey: ["admin", "runs", jobFilter, page],
    placeholderData: keepPreviousData,
    refetchInterval: 30_000,
    queryFn: async () => unwrap(await api.GET("/api/v1/admin/jobs/runs", { params: { query: { jobName: jobFilter || undefined, page, pageSize: RUNS_PAGE } } })),
  });
  const health = useQuery({ queryKey: ["admin", "health"], queryFn: async () => unwrap(await api.GET("/api/v1/admin/source-health", { params: { query: { take: 100 } } })) });
  const cost = useQuery({ queryKey: ["admin", "llm"], queryFn: async () => unwrap(await api.GET("/api/v1/admin/llm-calls", { params: { query: { recent: 10 } } })) });

  const runNow = useMutation({
    mutationFn: async (id: string) => unwrap(await api.POST("/api/v1/admin/jobs/{id}/run", { params: { path: { id } } })),
    onSuccess: (result) => setRequested((r) => ({ ...r, [result.jobId]: result.requestedAt })),
  });

  const jobNames = [...new Set([...(jobs.data ?? []).map((j) => j.id), ...(runs.data?.items ?? []).map((r) => r.jobName)])].sort();
  const pages = Math.max(1, Math.ceil((runs.data?.totalCount ?? 0) / RUNS_PAGE));

  return (
    <div className="space-y-6">
      <Card>
        <CardHeader>
          <CardTitle>Scheduled jobs</CardTitle>
          <CardDescription>Registered by the Worker; times are South African. Run now starts a job within about a minute.</CardDescription>
        </CardHeader>
        <CardContent>
          {jobs.isPending ? (
            <Skeleton className="h-32 w-full" />
          ) : jobs.isError ? (
            <p role="alert" className="text-sm text-destructive">
              {jobs.error.message}
            </p>
          ) : jobs.data.length === 0 ? (
            <p className="text-sm text-muted-foreground">No scheduled jobs found. Start the Worker once and it registers them.</p>
          ) : (
            <div className="overflow-x-auto">
              <table className="w-full text-left text-sm">
                <thead className="text-xs text-muted-foreground">
                  <tr>
                    <th className="py-1 pr-3 font-medium">Job</th>
                    <th className="py-1 pr-3 font-medium">Schedule</th>
                    <th className="py-1 pr-3 font-medium">Next</th>
                    <th className="py-1 pr-3 font-medium">Last</th>
                    <th className="py-1 font-medium">
                      <span className="sr-only">Actions</span>
                    </th>
                  </tr>
                </thead>
                <tbody>
                  {jobs.data.map((job) => (
                    <tr key={job.id} className="border-t align-middle" data-testid="recurring-job">
                      <td className="py-2 pr-3 font-mono text-xs">{job.id}</td>
                      <td className="py-2 pr-3 font-mono text-xs">{job.cron}</td>
                      <td className="py-2 pr-3 text-xs">{formatDateTime(job.nextExecution)}</td>
                      <td className="py-2 pr-3 text-xs">
                        {job.lastExecution ? formatDateTime(job.lastExecution) : "never"}
                        {job.lastJobState ? <span className="text-muted-foreground"> · {job.lastJobState}</span> : null}
                      </td>
                      <td className="py-2 text-right">
                        {requested[job.id] ? (
                          <span role="status" className="text-xs text-muted-foreground">
                            Requested
                          </span>
                        ) : (
                          <Button size="sm" variant="outline" disabled={runNow.isPending} onClick={() => runNow.mutate(job.id)}>
                            <Play /> Run now<span className="sr-only"> {job.id}</span>
                          </Button>
                        )}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
          {runNow.isError ? (
            <p role="alert" className="mt-2 text-sm text-destructive">
              {runNow.error.message}
            </p>
          ) : null}
        </CardContent>
      </Card>

      <div className="grid gap-6 lg:grid-cols-[minmax(0,1fr)_20rem]">
        <Card>
          <CardHeader>
            <CardTitle>Model cost per day</CardTitle>
            <CardDescription>
              {cost.data
                ? `${usd.format(cost.data.costUsd)} over the last 30 days · ${cost.data.calls} calls, ${cost.data.failed} failed`
                : "Last 30 days of model calls."}
            </CardDescription>
          </CardHeader>
          <CardContent className="space-y-4">
            {cost.isPending ? (
              <Skeleton className="h-48 w-full" />
            ) : cost.isError ? (
              <p role="alert" className="text-sm text-destructive">
                {cost.error.message}
              </p>
            ) : (
              <>
                <CostChart days={fillDays(cost.data.byDay, cost.data.from, cost.data.to)} />
                <div className="space-y-1 text-sm">
                  <div className="flex justify-between">
                    <span>Today</span>
                    <span className="tabular-nums">
                      {usd.format(cost.data.spentTodayUsd)} of {usd.format(cost.data.dailyCapUsd)} cap
                    </span>
                  </div>
                  <div
                    className="h-2 overflow-hidden rounded-full bg-muted"
                    role="meter"
                    aria-label="Today's spend against the daily cap"
                    aria-valuemin={0}
                    aria-valuemax={cost.data.dailyCapUsd}
                    aria-valuenow={cost.data.spentTodayUsd}
                  >
                    <div
                      className={cost.data.spentTodayUsd >= cost.data.dailyCapUsd ? "h-full bg-destructive" : "h-full bg-[var(--viz-series)]"}
                      style={{ width: `${cost.data.dailyCapUsd > 0 ? Math.min(100, (cost.data.spentTodayUsd / cost.data.dailyCapUsd) * 100) : 0}%` }}
                    />
                  </div>
                </div>
                {cost.data.byPurpose.length > 0 ? (
                  <table className="w-full text-left text-sm">
                    <caption className="pb-1 text-left text-xs text-muted-foreground">By purpose</caption>
                    <tbody>
                      {cost.data.byPurpose.map((b) => (
                        <tr key={b.key} className="border-t">
                          <td className="py-1 pr-3 font-mono text-xs">{b.key}</td>
                          <td className="py-1 pr-3 text-xs tabular-nums">{b.calls} calls</td>
                          <td className="py-1 text-right tabular-nums">{usd.format(b.costUsd)}</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                ) : null}
              </>
            )}
          </CardContent>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle>Sources</CardTitle>
            <CardDescription>Reachability, latency and schema drift of the tender feed.</CardDescription>
          </CardHeader>
          <CardContent>
            {health.isPending ? (
              <Skeleton className="h-24 w-full" />
            ) : health.isError ? (
              <p role="alert" className="text-sm text-destructive">
                {health.error.message}
              </p>
            ) : health.data.length === 0 ? (
              <p className="text-sm text-muted-foreground">No probes yet.</p>
            ) : (
              <ul className="space-y-3 text-sm">
                {latestPerSource(health.data).map((s) => (
                  <li key={s.source} className="space-y-1">
                    <div className="flex items-center gap-2">
                      {s.ok ? <CheckCircle2 className="size-4 text-emerald-600" aria-label="OK" /> : <XCircle className="size-4 text-destructive" aria-label="Failing" />}
                      <span className="font-medium">{s.source}</span>
                      {s.drift ? (
                        <Badge variant="outline" className="gap-1 border-amber-500/60 text-amber-700 dark:text-amber-400">
                          <AlertTriangle className="size-3" aria-hidden /> Schema drift
                        </Badge>
                      ) : null}
                    </div>
                    <div className="text-xs text-muted-foreground">
                      {formatDateTime(s.checkedAt)}
                      {s.httpStatus ? ` · HTTP ${s.httpStatus}` : ""}
                      {s.latencyMs !== null ? ` · ${s.latencyMs} ms` : ""}
                    </div>
                    {s.error ? <div className="text-xs text-destructive">{s.error}</div> : null}
                  </li>
                ))}
              </ul>
            )}
          </CardContent>
        </Card>
      </div>

      <Card>
        <CardHeader>
          <CardTitle>Job runs</CardTitle>
          <CardDescription>Every run lands here with its outcome and details, newest first.</CardDescription>
        </CardHeader>
        <CardContent className="space-y-3">
          <div className="flex flex-wrap items-center gap-2 text-sm">
            <Label htmlFor="run-filter">Job</Label>
            <select
              id="run-filter"
              className="h-8 rounded-md border bg-background px-2"
              value={jobFilter}
              onChange={(e) => {
                setPage(1);
                setJobFilter(e.target.value);
              }}
            >
              <option value="">All jobs</option>
              {jobNames.map((name) => (
                <option key={name} value={name}>
                  {name}
                </option>
              ))}
            </select>
            <span className="ml-auto text-muted-foreground">{runs.data ? `${runs.data.totalCount} runs` : ""}</span>
          </div>
          {runs.isPending ? (
            <Skeleton className="h-40 w-full" />
          ) : runs.isError ? (
            <p role="alert" className="text-sm text-destructive">
              {runs.error.message}
            </p>
          ) : runs.data.items.length === 0 ? (
            <p className="text-sm text-muted-foreground">No runs recorded.</p>
          ) : (
            <ul className="divide-y rounded-md border text-sm" aria-label="Job runs">
              {runs.data.items.map((run) => (
                <li key={run.id} className="p-3">
                  <details>
                    <summary className="flex cursor-pointer flex-wrap items-center gap-x-3 gap-y-1">
                      <span className="font-mono text-xs">{run.jobName}</span>
                      <StateBadge success={run.success} />
                      <span className="text-xs text-muted-foreground">
                        {formatDateTime(run.startedAt)} · {runDuration(run)} · {run.itemsProcessed} item{run.itemsProcessed === 1 ? "" : "s"}
                      </span>
                    </summary>
                    {run.error ? <p className="mt-2 text-xs text-destructive">{run.error}</p> : null}
                    {run.detailsJson ? <pre className="mt-2 max-h-64 overflow-auto rounded-md bg-muted p-2 font-mono text-xs">{run.detailsJson}</pre> : null}
                  </details>
                </li>
              ))}
            </ul>
          )}
          {pages > 1 ? (
            <div className="flex items-center justify-end gap-2 text-sm">
              <Button variant="outline" size="sm" disabled={page <= 1} onClick={() => setPage(page - 1)}>
                Previous
              </Button>
              <span>
                Page {page} of {pages}
              </span>
              <Button variant="outline" size="sm" disabled={page >= pages} onClick={() => setPage(page + 1)}>
                Next
              </Button>
            </div>
          ) : null}
        </CardContent>
      </Card>
    </div>
  );
}
