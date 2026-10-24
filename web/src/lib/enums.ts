/**
 * The API serialises enums as numbers (System.Text.Json defaults). These maps mirror TenderSync.Domain.Enums; keep
 * them in step when the Domain changes. Names are the labels people see.
 */
export const TenderStatus = {
  0: "Unknown",
  1: "Open",
  2: "Closing soon",
  3: "Closed",
  4: "Awarded",
  5: "Cancelled",
} as const;
export type TenderStatusValue = keyof typeof TenderStatus;

export const Province = {
  0: "Not stated",
  1: "Eastern Cape",
  2: "Free State",
  3: "Gauteng",
  4: "KwaZulu-Natal",
  5: "Limpopo",
  6: "Mpumalanga",
  7: "North West",
  8: "Northern Cape",
  9: "Western Cape",
  10: "National",
} as const;
export type ProvinceValue = keyof typeof Province;

export const ProcurementCategory = { 0: "Not stated", 1: "Goods", 2: "Services", 3: "Works" } as const;

export const MatchStatus = {
  0: "New",
  1: "Reviewing",
  2: "Dismissed",
  3: "Applying",
  4: "Applied",
  5: "Won",
  6: "Lost",
  7: "Expired",
} as const;
export type MatchStatusValue = keyof typeof MatchStatus;

export const TenderSort = { 0: "Closing soonest", 1: "Newest first", 2: "Most relevant" } as const;
export type TenderSortValue = keyof typeof TenderSort;

export function label<T extends Record<number, string>>(map: T, value: number | null | undefined, fallback = "—"): string {
  return value === null || value === undefined ? fallback : (map[value] ?? String(value));
}

/** Tone for status badges. */
export function tenderStatusTone(status: number): "default" | "secondary" | "destructive" | "outline" {
  switch (status) {
    case 2:
      return "destructive";
    case 1:
      return "default";
    case 4:
      return "secondary";
    default:
      return "outline";
  }
}
