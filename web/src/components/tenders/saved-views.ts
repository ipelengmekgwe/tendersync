/**
 * Saved views for /tenders (PLAN.md §11): a name plus the list's query string, kept in this browser's localStorage.
 * They are a per-person convenience; the URL itself is the shareable view.
 */
export interface SavedView {
  name: string;
  query: string;
}

export const SAVED_VIEWS_KEY = "tendersync.tenderViews.v1";
const MAX_VIEWS = 20;

export function parseSavedViews(raw: string | null): SavedView[] {
  if (!raw) {
    return [];
  }
  try {
    const value: unknown = JSON.parse(raw);
    if (!Array.isArray(value)) {
      return [];
    }
    return value
      .filter((v): v is SavedView => typeof v === "object" && v !== null && typeof (v as SavedView).name === "string" && typeof (v as SavedView).query === "string")
      .map((v) => ({ name: v.name.trim().slice(0, 60), query: v.query.replace(/^\?/, "") }))
      .filter((v) => v.name.length > 0)
      .slice(0, MAX_VIEWS);
  } catch {
    return [];
  }
}

/** Adds or replaces (same name, case-insensitive) a view; newest first. */
export function saveView(views: SavedView[], name: string, query: string): SavedView[] {
  const trimmed = name.trim().slice(0, 60);
  if (!trimmed) {
    return views;
  }
  const rest = views.filter((v) => v.name.toLowerCase() !== trimmed.toLowerCase());
  return [{ name: trimmed, query: query.replace(/^\?/, "") }, ...rest].slice(0, MAX_VIEWS);
}

export function removeView(views: SavedView[], name: string): SavedView[] {
  return views.filter((v) => v.name !== name);
}

export function readSavedViews(): SavedView[] {
  try {
    return parseSavedViews(window.localStorage.getItem(SAVED_VIEWS_KEY));
  } catch {
    return [];
  }
}

export function writeSavedViews(views: SavedView[]): void {
  try {
    window.localStorage.setItem(SAVED_VIEWS_KEY, JSON.stringify(views));
  } catch {
    // Private windows and blocked storage: the views simply do not persist.
  }
}
