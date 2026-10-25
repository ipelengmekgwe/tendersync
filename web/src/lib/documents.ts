/** Links to a tender document: the stored copy goes through the authenticated proxy, never straight to the API. */
export function documentDownloadHref(tenderId: string, documentId: string): string {
  return `/api/proxy/tenders/${encodeURIComponent(tenderId)}/documents/${encodeURIComponent(documentId)}/download`;
}

/** The in-app viewer, opened at a page (1-based) with the quote the requirement came from. */
export function documentViewerHref(tenderId: string, documentId: string, page?: number | null, quote?: string | null): string {
  const params = new URLSearchParams();
  if (page && page > 0) {
    params.set("page", String(page));
  }
  if (quote) {
    params.set("quote", quote.slice(0, 300));
  }
  const query = params.toString();
  return `/tenders/${encodeURIComponent(tenderId)}/documents/${encodeURIComponent(documentId)}${query ? `?${query}` : ""}`;
}

/** Page numbers from the address bar: a positive integer, or 1. */
export function parsePage(value: string | string[] | undefined): number {
  const raw = Array.isArray(value) ? value[0] : value;
  const n = Number(raw);
  return Number.isInteger(n) && n > 0 && n < 100_000 ? n : 1;
}

export function isPdf(format: string | null | undefined, title?: string | null): boolean {
  return (format ?? "").toLowerCase().includes("pdf") || (title ?? "").toLowerCase().endsWith(".pdf");
}

export function formatBytes(bytes: number | null | undefined): string {
  if (bytes === null || bytes === undefined) {
    return "";
  }
  if (bytes < 1024) {
    return `${bytes} B`;
  }
  const kb = bytes / 1024;
  if (kb < 1024) {
    return `${kb.toFixed(0)} KB`;
  }
  return `${(kb / 1024).toFixed(1)} MB`;
}
