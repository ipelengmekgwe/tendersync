/**
 * Server-side configuration. The browser never talks to the .NET API directly: route handlers under /api/* call it
 * with the tokens held in httpOnly cookies (ADR-011), so only the server needs the API address.
 */
export const config = {
  /** Base URL of TenderSync.Api, without a trailing slash. */
  apiBaseUrl: (process.env.API_BASE_URL ?? "http://localhost:5000").replace(/\/+$/, ""),
  /** Cookies are marked Secure outside development so they only travel over HTTPS. */
  secureCookies: process.env.AUTH_COOKIE_SECURE
    ? process.env.AUTH_COOKIE_SECURE === "true"
    : process.env.NODE_ENV === "production",
} as const;
