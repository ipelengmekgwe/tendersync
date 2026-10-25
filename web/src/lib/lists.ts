/** Comma- or line-separated text to a clean list: trimmed, empty entries dropped, duplicates (case-insensitive) removed. */
export function parseList(text: string): string[] {
  const seen = new Set<string>();
  const out: string[] = [];
  for (const raw of text.split(/[\n,]/)) {
    const value = raw.trim();
    const key = value.toLowerCase();
    if (value && !seen.has(key)) {
      seen.add(key);
      out.push(value);
    }
  }
  return out;
}

export function formatList(values: readonly string[] | null | undefined, separator = ", "): string {
  return (values ?? []).join(separator);
}

/** A date input (yyyy-mm-dd, South African calendar day) to the ISO instant the API stores: midnight SAST. Empty → null. */
export function dateInputToIso(value: string): string | null {
  return /^\d{4}-\d{2}-\d{2}$/.test(value) ? `${value}T00:00:00+02:00` : null;
}

/** An ISO instant to the SAST calendar day for a date input. */
export function isoToDateInput(iso: string | null | undefined): string {
  if (!iso) {
    return "";
  }
  return new Intl.DateTimeFormat("en-CA", { timeZone: "Africa/Johannesburg", year: "numeric", month: "2-digit", day: "2-digit" }).format(new Date(iso));
}

/** Expiry state for badges: expired, expiring within the window, valid, or none. */
export function expiryState(iso: string | null | undefined, now: Date = new Date(), windowDays = 30): "expired" | "soon" | "valid" | "none" {
  if (!iso) {
    return "none";
  }
  const at = new Date(iso).getTime();
  if (at <= now.getTime()) {
    return "expired";
  }
  return at - now.getTime() <= windowDays * 86_400_000 ? "soon" : "valid";
}
