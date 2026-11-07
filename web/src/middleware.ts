import { type NextRequest, NextResponse } from "next/server";
import { REFRESH_COOKIE } from "@/lib/auth/cookies";
import { isCrossSiteWrite } from "@/lib/auth/csrf";
import { safeNextPath } from "@/lib/auth/redirect";

/**
 * Pages need a session cookie; /login and the auth route handlers do not. The proxy enforces auth itself (the API
 * answers 401), so this only decides redirects. Presence of the refresh cookie is the signal: the access cookie may
 * have expired while the refresh token is still good, and the proxy rotates it on the next call. Route handlers act on
 * the session cookies, so writes to them from another site are refused here (login CSRF, forced logout).
 */
const PUBLIC_PATHS = ["/login", "/api/auth/login", "/api/auth/refresh", "/api/auth/logout"];

export function middleware(request: NextRequest): NextResponse {
  const { pathname, search } = request.nextUrl;
  const signedIn = Boolean(request.cookies.get(REFRESH_COOKIE)?.value);

  if (pathname.startsWith("/api/") && isCrossSiteWrite(request.method, request.headers)) {
    return secure(request, NextResponse.json({ title: "Cross-site requests are not accepted." }, { status: 403 }));
  }
  if (pathname === "/login" && signedIn) {
    return secure(request, NextResponse.redirect(new URL(safeNextPath(request.nextUrl.searchParams.get("next")), request.url)));
  }
  if (PUBLIC_PATHS.some((p) => pathname === p || pathname.startsWith(p + "/")) || pathname.startsWith("/api/proxy")) {
    return secure(request, NextResponse.next());
  }
  if (!signedIn) {
    const login = new URL("/login", request.url);
    if (pathname !== "/") {
      login.searchParams.set("next", pathname + search);
    }
    return secure(request, NextResponse.redirect(login));
  }
  return secure(request, NextResponse.next());
}

/**
 * HSTS only when the request arrived over HTTPS (the Azure ingress terminates TLS and says so in X-Forwarded-Proto). It
 * is set here rather than in next.config because whether the site is served over HTTPS is known only at run time.
 */
function secure(request: NextRequest, response: NextResponse): NextResponse {
  if ((request.headers.get("x-forwarded-proto") ?? request.nextUrl.protocol.replace(":", "")) === "https") {
    response.headers.set("strict-transport-security", "max-age=31536000; includeSubDomains");
  }
  return response;
}

export const config = {
  matcher: ["/((?!_next/static|_next/image|favicon.ico|icon.svg|robots.txt).*)"],
};
