const SAFE_METHODS = new Set(["GET", "HEAD", "OPTIONS"]);

/**
 * Whether a request that would change state came from another site. The auth and proxy route handlers act on the
 * httpOnly cookies, so a cross-site form post could otherwise sign someone into an attacker's account or sign them out.
 * Browsers send Sec-Fetch-Site on every request; older ones send Origin on cross-site posts. A request with neither
 * comes from a non-browser client, which carries no victim's cookies, so it is let through.
 */
export function isCrossSiteWrite(method: string, headers: Headers): boolean {
  if (SAFE_METHODS.has(method.toUpperCase())) {
    return false;
  }

  const site = headers.get("sec-fetch-site");
  if (site) {
    return site !== "same-origin" && site !== "none";
  }

  const origin = headers.get("origin");
  if (!origin) {
    return false;
  }

  const host = headers.get("x-forwarded-host") ?? headers.get("host");
  try {
    return new URL(origin).host !== host;
  } catch {
    return true;
  }
}
