function escapeHtml(text: string): string {
  return text.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;").replace(/"/g, "&quot;").replace(/'/g, "&#39;");
}

function normalise(text: string): string {
  return text.toLowerCase().replace(/\s+/g, " ").trim();
}

/**
 * Text-layer renderer for the PDF viewer: returns the item's text as escaped HTML, wrapped in <mark> when it is part of
 * the requirement's source quote. Short fragments (three characters or fewer) are not marked; they match too often.
 */
export function highlightQuote(itemText: string, quote: string | null | undefined): string {
  const escaped = escapeHtml(itemText);
  if (!quote) {
    return escaped;
  }
  const item = normalise(itemText);
  if (item.length <= 3) {
    return escaped;
  }
  const q = normalise(quote);
  return q.includes(item) || item.includes(q) ? `<mark>${escaped}</mark>` : escaped;
}
