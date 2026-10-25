/**
 * The browser's address chain to pass on to the API. Every API call comes from this server, so without it the API would
 * rate-limit and audit every user as the dashboard container. The API trusts the header only from private addresses and
 * walks it to the first public one (ApiServiceExtensions), so a value a browser forged itself does not help it.
 */
export function forwardedHeaders(incoming: Headers | null | undefined): Record<string, string> {
  const forwardedFor = incoming?.get("x-forwarded-for");
  return forwardedFor ? { "x-forwarded-for": forwardedFor } : {};
}
