"use client";

import { useState } from "react";
import { keepPreviousData, useQuery } from "@tanstack/react-query";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Skeleton } from "@/components/ui/skeleton";
import { api } from "@/lib/api/client";
import { unwrap } from "@/lib/api/problem";
import { formatDateTime } from "@/lib/format";

const PAGE_SIZE = 50;

/** "UpdateMatchCommand" → "Update match". */
export function humaniseAction(action: string): string {
  const words = action.replace(/(Command|Query)$/, "").replace(/([a-z0-9])([A-Z])/g, "$1 $2").toLowerCase();
  return words.charAt(0).toUpperCase() + words.slice(1);
}

function prettyJson(json: string | null): string {
  if (!json) {
    return "";
  }
  try {
    return JSON.stringify(JSON.parse(json), null, 2);
  } catch {
    return json;
  }
}

/** /admin/audit: every command anyone ran (PLAN.md §12), newest first, with the request as recorded ([Sensitive] fields redacted). */
export function AuditLog() {
  const [filters, setFilters] = useState({ action: "", entityType: "" });
  const [applied, setApplied] = useState(filters);
  const [page, setPage] = useState(1);

  const logs = useQuery({
    queryKey: ["admin", "audit", applied, page],
    placeholderData: keepPreviousData,
    queryFn: async () =>
      unwrap(
        await api.GET("/api/v1/admin/audit-logs", {
          params: { query: { action: applied.action || undefined, entityType: applied.entityType || undefined, page, pageSize: PAGE_SIZE } },
        }),
      ),
  });

  const pages = Math.max(1, Math.ceil((logs.data?.totalCount ?? 0) / PAGE_SIZE));

  return (
    <Card>
      <CardHeader>
        <CardTitle>Audit log</CardTitle>
        <CardDescription>Who did what, when and from where. Secrets in requests are stored as [redacted].</CardDescription>
      </CardHeader>
      <CardContent className="space-y-4">
        <form
          className="flex flex-wrap items-end gap-3"
          onSubmit={(e) => {
            e.preventDefault();
            setPage(1);
            setApplied(filters);
          }}
        >
          <div className="space-y-1">
            <Label htmlFor="audit-action">Action (exact)</Label>
            <Input id="audit-action" className="w-56" value={filters.action} placeholder="UpdateMatchCommand" onChange={(e) => setFilters({ ...filters, action: e.target.value.trim() })} />
          </div>
          <div className="space-y-1">
            <Label htmlFor="audit-entity">Entity type (exact)</Label>
            <Input id="audit-entity" className="w-48" value={filters.entityType} placeholder="TenderMatch" onChange={(e) => setFilters({ ...filters, entityType: e.target.value.trim() })} />
          </div>
          <Button type="submit" variant="secondary">
            Filter
          </Button>
          <span className="ml-auto text-sm text-muted-foreground" aria-live="polite">
            {logs.data ? `${logs.data.totalCount} entr${logs.data.totalCount === 1 ? "y" : "ies"}` : ""}
          </span>
        </form>

        {logs.isPending ? (
          <Skeleton className="h-48 w-full" />
        ) : logs.isError ? (
          <p role="alert" className="text-sm text-destructive">
            {logs.error.message}
          </p>
        ) : logs.data.items.length === 0 ? (
          <p className="text-sm text-muted-foreground">No entries.</p>
        ) : (
          <ul className="divide-y rounded-md border text-sm" aria-label="Audit entries">
            {logs.data.items.map((entry) => (
              <li key={entry.id} className="p-3">
                <details>
                  <summary className="flex cursor-pointer flex-wrap items-center gap-x-3 gap-y-1">
                    <span className="font-medium">{humaniseAction(entry.action)}</span>
                    <span className="text-muted-foreground">by {entry.actorName}</span>
                    <span className="text-xs text-muted-foreground">
                      {formatDateTime(entry.at)}
                      {entry.ip ? ` · ${entry.ip}` : ""}
                      {entry.entityId ? ` · ${entry.entityType} ${entry.entityId}` : ""}
                    </span>
                  </summary>
                  {entry.afterJson ? <pre className="mt-2 max-h-80 overflow-auto rounded-md bg-muted p-2 font-mono text-xs">{prettyJson(entry.afterJson)}</pre> : null}
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
  );
}
