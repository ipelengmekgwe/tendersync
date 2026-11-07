import { type NextRequest, NextResponse } from "next/server";
import { REFRESH_COOKIE } from "@/lib/auth/cookies";
import { safeNextPath } from "@/lib/auth/redirect";

/**
 * Pages need a session cookie; /login and the auth route handlers do not. The proxy enforces auth itself (the API
 * answers 401), so this only decides redirects. Presence of the refresh cookie is the signal: the access cookie may
 * have expired while the refresh token is still good, and the proxy rotates it on the next call.
 */
const PUBLIC_PATHS = ["/login", "/api/auth/login", "/api/auth/refresh", "/api/auth/logout"];

export function middleware(request: NextRequest): NextResponse {
  const { pathname, search } = request.nextUrl;
  const signedIn = Boolean(request.cookies.get(REFRESH_COOKIE)?.value);

  if (pathname === "/login" && signedIn) {
    return NextResponse.redirect(new URL(safeNextPath(request.nextUrl.searchParams.get("next")), request.url));
  }
  if (PUBLIC_PATHS.some((p) => pathname === p || pathname.startsWith(p + "/")) || pathname.startsWith("/api/proxy")) {
    return NextResponse.next();
  }
  if (!signedIn) {
    const login = new URL("/login", request.url);
    if (pathname !== "/") {
      login.searchParams.set("next", pathname + search);
    }
    return NextResponse.redirect(login);
  }
  return NextResponse.next();
}

export const config = {
  matcher: ["/((?!_next/static|_next/image|favicon.ico|icon.svg|robots.txt).*)"],
};
