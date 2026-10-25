"use client";

import Link from "next/link";
import { usePathname, useRouter } from "next/navigation";
import { keepPreviousData, useQuery } from "@tanstack/react-query";
import { CalendarClock, Users } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Label } from "@/components/ui/label";
import { Skeleton } from "@/components/ui/skeleton";
import { MatchStatusControl } from "@/components/matches/match-status-control";
import { api } from "@/lib/api/client";
import type { MatchListItem } from "@/lib/api/types";
import { MatchSort, type MatchSortValue, Province, QualificationVerdict, label, verdictTone } from "@/lib/enums";
import { closingIn, formatDateTime } from "@/lib/format";
import { INBOX_TABS, type InboxTab } from "@/lib/matches";
import { cn } from "@/lib/utils";

export interface InboxFilters {
  tab: InboxTab;
  sort: MatchSortValue;
  closingSoon: boolean;
  page: number;
}

const PAGE_SIZE = 25;

export function inboxHref(pathname: string, filters: InboxFilters): string {
  const params = new URLSearchParams();
  if (filters.tab.key !== "new") params.set("tab", filters.tab.key);
  if (filters.sort !== 0) params.set("sort", String(filters.sort));
  if (filters.closingSoon) params.set("soon", "true");
  if (filters.page !== 1) params.set("page", String(filters.page));
  const query = params.toString();
  return query ? `${pathname}?${query}` : pathname;
}

export function MatchesInbox({ filters, canEdit }: { filters: InboxFilters; canEdit: boolean }) {
  const router = useRouter();
  const pathname = usePathname();

  const query = useQuery({
    queryKey: ["matches", "inbox", filters.tab.status, filters.sort, filters.closingSoon, filters.page],
    placeholderData: keepPreviousData,
    queryFn: async () => {
      const { data, error, response } = await api.GET("/api/v1/matches", {
        params: {
          query: {
            Status: filters.tab.status,
            Sort: filters.sort,
            ClosingWithinDays: filters.closingSoon ? 7 : undefined,
            Page: filters.page,
            PageSize: PAGE_SIZE,
          },
        },
      });
      if (error || !data) {
        throw new Error(String(response.status));
      }
      return data;
    },
  });

  const go = (next: Partial<InboxFilters>) => router.push(inboxHref(pathname, { ...filters, page: 1, ...next }));
  const counts = new Map((query.data?.counts ?? []).map((c) => [c.status, c.count]));
  const total = query.data?.totalCount ?? 0;
  const pages = Math.max(1, Math.ceil(total / PAGE_SIZE));

  return (
    <div className="space-y-4">
      <nav aria-label="Match status" className="-mx-1 flex gap-1 overflow-x-auto overflow-y-hidden border-b px-1">
        {INBOX_TABS.map((tab) => {
          const active = tab.key === filters.tab.key;
          const count = counts.get(tab.status);
          return (
            <Link
              key={tab.key}
              href={inboxHref(pathname, { ...filters, tab, page: 1 })}
              aria-current={active ? "page" : undefined}
              className={cn(
                "-mb-px flex items-center gap-2 border-b-2 px-3 py-2 text-sm whitespace-nowrap",
                active ? "border-primary font-medium text-foreground" : "border-transparent text-muted-foreground hover:text-foreground",
              )}
            >
              {tab.label}
              {count !== undefined ? (
                <span className="rounded-full bg-muted px-1.5 text-xs tabular-nums" aria-label={`${count} matches`}>
                  {count}
                </span>
              ) : null}
            </Link>
          );
        })}
      </nav>

      <div className="flex flex-wrap items-center gap-4 text-sm">
        <div className="flex items-center gap-2">
          <Label htmlFor="match-sort">Sort</Label>
          <select
            id="match-sort"
            className="h-8 rounded-md border bg-background px-2"
            value={filters.sort}
            onChange={(e) => go({ sort: Number(e.target.value) as MatchSortValue })}
          >
            {Object.entries(MatchSort).map(([value, name]) => (
              <option key={value} value={value}>
                {name}
              </option>
            ))}
          </select>
        </div>
        <label className="flex items-center gap-2">
          <input type="checkbox" checked={filters.closingSoon} onChange={(e) => go({ closingSoon: e.target.checked })} />
          Closing within 7 days
        </label>
        <span className="ml-auto text-muted-foreground" aria-live="polite">
          {query.isPending ? "Loading…" : `${total} match${total === 1 ? "" : "es"}`}
        </span>
      </div>

      {query.isError ? (
        <p role="alert" className="rounded-md border border-destructive/40 bg-destructive/10 p-3 text-sm">
          The inbox could not be loaded (API responded {query.error.message}). Try again or sign in afresh.
        </p>
      ) : query.isPending ? (
        <div className="space-y-2" aria-busy="true">
          {Array.from({ length: 4 }).map((_, i) => (
            <Skeleton key={i} className="h-24 w-full" />
          ))}
        </div>
      ) : query.data.items.length === 0 ? (
        <p className="rounded-md border border-dashed p-6 text-center text-sm text-muted-foreground">
          Nothing here. {filters.tab.key === "new" ? "New matches appear after each ingest and classification run." : null}
        </p>
      ) : (
        <ul className="divide-y rounded-lg border" aria-label={`${filters.tab.label} matches`}>
          {query.data.items.map((item) => (
            <MatchRow key={item.id} item={item} canEdit={canEdit} />
          ))}
        </ul>
      )}

      {pages > 1 ? (
        <div className="flex items-center justify-end gap-2 text-sm">
          <Button variant="outline" size="sm" disabled={filters.page <= 1} onClick={() => go({ page: filters.page - 1 })}>
            Previous
          </Button>
          <span>
            Page {filters.page} of {pages}
          </span>
          <Button variant="outline" size="sm" disabled={filters.page >= pages} onClick={() => go({ page: filters.page + 1 })}>
            Next
          </Button>
        </div>
      ) : null}
    </div>
  );
}

function MatchRow({ item, canEdit }: { item: MatchListItem; canEdit: boolean }) {
  const countdown = closingIn(item.closingAt);
  return (
    <li className="grid gap-3 p-4 md:grid-cols-[minmax(0,1fr)_auto] md:items-start" data-testid="match-row">
      <div className="min-w-0 space-y-1.5">
        <div className="flex flex-wrap items-center gap-x-2 text-xs text-muted-foreground">
          <span className="font-mono">{item.bidNumber}</span>
          <span aria-hidden>·</span>
          <span>{item.buyerName}</span>
          <span aria-hidden>·</span>
          <span>{label(Province, item.province)}</span>
        </div>
        <Link href={`/tenders/${item.tenderId}`} className="block font-medium leading-snug hover:underline">
          {item.title}
        </Link>
        <div className="flex flex-wrap items-center gap-2 text-sm">
          <Badge variant="outline" title="Relevance from classification, 0 to 100">
            Relevance {item.relevance}
          </Badge>
          {item.verdict !== null ? (
            <Badge variant={verdictTone(item.verdict)}>
              {label(QualificationVerdict, item.verdict)}
              {item.score !== null ? ` · ${item.score}` : ""}
            </Badge>
          ) : (
            <Badge variant="outline">Not qualified yet</Badge>
          )}
          <span className={cn("inline-flex items-center gap-1", countdown === "today" || countdown === "tomorrow" ? "font-medium text-destructive" : "text-muted-foreground")}>
            <CalendarClock className="size-3.5" aria-hidden />
            {item.closingAt ? `Closes ${formatDateTime(item.closingAt)}${countdown ? ` (${countdown})` : ""}` : "Closing date not stated"}
          </span>
          {item.briefingCompulsory ? (
            <span className="inline-flex items-center gap-1 text-muted-foreground">
              <Users className="size-3.5" aria-hidden />
              Compulsory briefing {formatDateTime(item.briefingAt)}
            </span>
          ) : null}
        </div>
        {item.notes ? <p className="line-clamp-2 text-xs text-muted-foreground">Notes: {item.notes}</p> : null}
      </div>
      <MatchStatusControl compact canEdit={canEdit} match={{ id: item.id, status: item.status, notes: item.notes, dismissReason: item.dismissReason }} />
    </li>
  );
}
