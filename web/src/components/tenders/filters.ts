import type { TenderSortValue } from "@/lib/enums";

export type TenderSearchParams = Record<string, string | string[] | undefined>;

export interface TenderFilters {
  q: string;
  status: number | null;
  province: number | null;
  matchedOnly: boolean;
  nearMisses: boolean;
  /** Only tenders closing between now and now + this many days. */
  closingWithin: ClosingWithin | null;
  sort: TenderSortValue;
  page: number;
  pageSize: number;
}

export const PAGE_SIZES = [25, 50, 100] as const;

export const CLOSING_WITHIN = [7, 14, 30] as const;
export type ClosingWithin = (typeof CLOSING_WITHIN)[number];

function first(value: string | string[] | undefined): string | undefined {
  return Array.isArray(value) ? value[0] : value;
}

function int(value: string | undefined, fallback: number | null, min: number, max: number): number | null {
  if (value === undefined || value === "") {
    return fallback;
  }
  const n = Number(value);
  return Number.isInteger(n) && n >= min && n <= max ? n : fallback;
}

/** Reads the URL into a filter object, dropping anything out of range so the API never gets a 400 from the address bar. */
export function parseTenderFilters(params: TenderSearchParams): TenderFilters {
  return {
    q: first(params.q)?.trim() ?? "",
    status: int(first(params.status), null, 0, 5),
    province: int(first(params.province), null, 0, 10),
    matchedOnly: first(params.matchedOnly) === "true",
    nearMisses: first(params.nearMisses) === "true",
    closingWithin: (CLOSING_WITHIN as readonly number[]).includes(Number(first(params.closingWithin))) ? (Number(first(params.closingWithin)) as ClosingWithin) : null,
    sort: (int(first(params.sort), 0, 0, 2) ?? 0) as TenderSortValue,
    page: int(first(params.page), 1, 1, 100_000) ?? 1,
    pageSize: (PAGE_SIZES as readonly number[]).includes(Number(first(params.pageSize))) ? Number(first(params.pageSize)) : 25,
  };
}

/** The inverse: only non-default values go into the URL. */
export function toSearchParams(filters: TenderFilters): URLSearchParams {
  const params = new URLSearchParams();
  if (filters.q) params.set("q", filters.q);
  if (filters.status !== null) params.set("status", String(filters.status));
  if (filters.province !== null) params.set("province", String(filters.province));
  if (filters.matchedOnly) params.set("matchedOnly", "true");
  if (filters.nearMisses) params.set("nearMisses", "true");
  if (filters.closingWithin !== null) params.set("closingWithin", String(filters.closingWithin));
  if (filters.sort !== 0) params.set("sort", String(filters.sort));
  if (filters.page !== 1) params.set("page", String(filters.page));
  if (filters.pageSize !== 25) params.set("pageSize", String(filters.pageSize));
  return params;
}

/** Query-string parameters for GET /api/v1/tenders, named as the API binds them. */
export function toApiQuery(filters: TenderFilters, now: Date = new Date()) {
  return {
    ClosingFrom: filters.closingWithin !== null ? now.toISOString() : undefined,
    ClosingTo: filters.closingWithin !== null ? new Date(now.getTime() + filters.closingWithin * 86_400_000).toISOString() : undefined,
    Q: filters.q || undefined,
    Status: filters.status ?? undefined,
    Province: filters.province ?? undefined,
    MatchedOnly: filters.matchedOnly || undefined,
    NearMisses: filters.nearMisses || undefined,
    Sort: filters.sort,
    Page: filters.page,
    PageSize: filters.pageSize,
  };
}
