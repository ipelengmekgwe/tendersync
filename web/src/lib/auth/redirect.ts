/**
 * Where to send someone after sign-in, from the `next` query parameter. Only same-site paths are allowed: "//host" and
 * "/\host" are protocol-relative URLs that browsers resolve to another origin, so they fall back to "/" like any absolute
 * URL does. Prevents the login page being used as an open redirect.
 */
export function safeNextPath(value: string | null | undefined): string {
  if (!value || !value.startsWith("/") || value.startsWith("//") || value.startsWith("/\\")) {
    return "/";
  }
  return /[\u0000-\u001f]/.test(value) ? "/" : value;
}
