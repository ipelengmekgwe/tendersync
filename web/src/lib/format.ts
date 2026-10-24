/** Display helpers. Storage is UTC; everything shown to a person is SAST (PLAN.md: display in SAST only at the edge). */
export const SAST = "Africa/Johannesburg";

const dateTime = new Intl.DateTimeFormat("en-ZA", {
  timeZone: SAST,
  weekday: "short",
  day: "numeric",
  month: "short",
  year: "numeric",
  hour: "2-digit",
  minute: "2-digit",
  hour12: false,
});

const dateOnly = new Intl.DateTimeFormat("en-ZA", { timeZone: SAST, day: "numeric", month: "short", year: "numeric" });

export function formatDateTime(iso: string | null | undefined): string {
  return iso ? dateTime.format(new Date(iso)) : "not stated";
}

export function formatDate(iso: string | null | undefined): string {
  return iso ? dateOnly.format(new Date(iso)) : "not stated";
}

/** Whole South African calendar days from now until the date (negative when past). */
export function daysUntil(iso: string, now: Date = new Date()): number {
  const toSastDay = (d: Date): number => {
    const parts = new Intl.DateTimeFormat("en-CA", { timeZone: SAST, year: "numeric", month: "2-digit", day: "2-digit" }).format(d);
    return Math.floor(Date.UTC(Number(parts.slice(0, 4)), Number(parts.slice(5, 7)) - 1, Number(parts.slice(8, 10))) / 86_400_000);
  };
  return toSastDay(new Date(iso)) - toSastDay(now);
}

/** "closed", "today", "tomorrow", "in 12 days" or "" when no date. */
export function closingIn(iso: string | null | undefined, now: Date = new Date()): string {
  if (!iso) {
    return "";
  }
  if (new Date(iso).getTime() <= now.getTime()) {
    return "closed";
  }
  const days = daysUntil(iso, now);
  if (days <= 0) {
    return "today";
  }
  if (days === 1) {
    return "tomorrow";
  }
  return `in ${days} days`;
}
