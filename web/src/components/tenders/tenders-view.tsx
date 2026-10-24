"use client";

import { usePathname, useRouter } from "next/navigation";
import { useEffect, useState } from "react";
import { keepPreviousData, useQuery } from "@tanstack/react-query";
import { Search } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Skeleton } from "@/components/ui/skeleton";
import { api } from "@/lib/api/client";
import { Province, TenderSort, TenderStatus, type TenderSortValue } from "@/lib/enums";
import { PAGE_SIZES, type TenderFilters, toApiQuery, toSearchParams } from "@/components/tenders/filters";
import { TenderTable } from "@/components/tenders/tender-table";

export function TendersView({ filters }: { filters: TenderFilters }) {
  const router = useRouter();
  const pathname = usePathname();
  const [q, setQ] = useState(filters.q);
  useEffect(() => setQ(filters.q), [filters.q]);

  const query = useQuery({
    queryKey: ["tenders", filters],
    placeholderData: keepPreviousData,
    queryFn: async () => {
      const { data, error, response } = await api.GET("/api/v1/tenders", { params: { query: toApiQuery(filters) } });
      if (error || !data) {
        throw new Error(String(response.status));
      }
      return data;
    },
  });

  function apply(next: Partial<TenderFilters>) {
    const merged = { ...filters, ...next, page: next.page ?? 1 };
    const params = toSearchParams(merged).toString();
    router.push(params ? `${pathname}?${params}` : pathname);
  }

  const total = query.data?.totalCount ?? 0;
  const pages = Math.max(1, Math.ceil(total / filters.pageSize));

  return (
    <div className="space-y-4">
      <form
        className="grid gap-3 md:grid-cols-[1fr_auto_auto_auto] md:items-end"
        onSubmit={(e) => {
          e.preventDefault();
          apply({ q });
        }}
      >
        <div className="space-y-1">
          <Label htmlFor="q">Search</Label>
          <div className="flex gap-2">
            <Input id="q" placeholder="Bid number, title or description" value={q} onChange={(e) => setQ(e.target.value)} />
            <Button type="submit" variant="secondary" aria-label="Search">
              <Search className="size-4" />
            </Button>
          </div>
        </div>
        <div className="space-y-1">
          <Label htmlFor="status">Status</Label>
          <select
            id="status"
            className="h-9 rounded-md border bg-background px-2 text-sm"
            value={filters.status ?? ""}
            onChange={(e) => apply({ status: e.target.value === "" ? null : Number(e.target.value) })}
          >
            <option value="">Any status</option>
            {Object.entries(TenderStatus).map(([value, name]) => (
              <option key={value} value={value}>
                {name}
              </option>
            ))}
          </select>
        </div>
        <div className="space-y-1">
          <Label htmlFor="province">Province</Label>
          <select
            id="province"
            className="h-9 rounded-md border bg-background px-2 text-sm"
            value={filters.province ?? ""}
            onChange={(e) => apply({ province: e.target.value === "" ? null : Number(e.target.value) })}
          >
            <option value="">Any province</option>
            {Object.entries(Province).map(([value, name]) => (
              <option key={value} value={value}>
                {name}
              </option>
            ))}
          </select>
        </div>
        <div className="space-y-1">
          <Label htmlFor="sort">Sort</Label>
          <select
            id="sort"
            className="h-9 rounded-md border bg-background px-2 text-sm"
            value={filters.sort}
            onChange={(e) => apply({ sort: Number(e.target.value) as TenderSortValue })}
          >
            {Object.entries(TenderSort).map(([value, name]) => (
              <option key={value} value={value}>
                {name}
              </option>
            ))}
          </select>
        </div>
      </form>

      <div className="flex flex-wrap items-center gap-4 text-sm">
        <label className="flex items-center gap-2">
          <input type="checkbox" checked={filters.matchedOnly} onChange={(e) => apply({ matchedOnly: e.target.checked, nearMisses: false })} />
          Matched only
        </label>
        <label className="flex items-center gap-2">
          <input type="checkbox" checked={filters.nearMisses} onChange={(e) => apply({ nearMisses: e.target.checked, matchedOnly: false })} />
          Near misses
        </label>
        <span className="ml-auto text-muted-foreground" aria-live="polite">
          {query.isPending ? "Loading…" : `${total.toLocaleString("en-ZA")} tender${total === 1 ? "" : "s"}`}
        </span>
      </div>

      {query.isError ? (
        <p role="alert" className="rounded-md border border-destructive/40 bg-destructive/10 p-3 text-sm">
          The list could not be loaded (API responded {query.error.message}). Try again or sign in afresh.
        </p>
      ) : query.isPending ? (
        <div className="space-y-2" aria-busy="true">
          {Array.from({ length: 6 }).map((_, i) => (
            <Skeleton key={i} className="h-10 w-full" />
          ))}
        </div>
      ) : (
        <TenderTable items={query.data.items} />
      )}

      <div className="flex flex-wrap items-center justify-between gap-2 text-sm">
        <div className="flex items-center gap-2">
          <Label htmlFor="pageSize">Per page</Label>
          <select
            id="pageSize"
            className="h-8 rounded-md border bg-background px-2"
            value={filters.pageSize}
            onChange={(e) => apply({ pageSize: Number(e.target.value) })}
          >
            {PAGE_SIZES.map((size) => (
              <option key={size} value={size}>
                {size}
              </option>
            ))}
          </select>
        </div>
        <div className="flex items-center gap-2">
          <Button variant="outline" size="sm" disabled={filters.page <= 1} onClick={() => apply({ page: filters.page - 1 })}>
            Previous
          </Button>
          <span>
            Page {filters.page} of {pages}
          </span>
          <Button variant="outline" size="sm" disabled={filters.page >= pages} onClick={() => apply({ page: filters.page + 1 })}>
            Next
          </Button>
        </div>
      </div>
    </div>
  );
}
