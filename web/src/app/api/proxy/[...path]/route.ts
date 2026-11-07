import { cookies } from "next/headers";
import { type NextRequest, NextResponse } from "next/server";
import { ApiError, authApi } from "@/lib/auth/api-auth";
import { ACCESS_COOKIE, REFRESH_COOKIE, clearAuthCookies, setAuthCookies } from "@/lib/auth/cookies";
import { forwardedHeaders } from "@/lib/api/forwarded";
import { config } from "@/lib/config";

/**
 * Forwards /api/proxy/{path} to {API}/api/v1/{path} with the bearer token from the httpOnly cookie, so client components
 * can use the generated client without ever seeing a token. On a 401 the refresh token is rotated once and the call
 * retried; a failed refresh clears the cookies so the middleware sends the user back to /login.
 */
const FORWARDED_REQUEST_HEADERS = ["content-type", "accept", "if-none-match", "range"];
const FORWARDED_RESPONSE_HEADERS = ["content-type", "content-disposition", "content-length", "content-range", "accept-ranges", "etag", "cache-control", "location"];

async function forward(request: NextRequest, path: string[], accessToken: string | null, body: ArrayBuffer | null): Promise<Response> {
  const target = new URL(`${config.apiBaseUrl}/api/v1/${path.map(encodeURIComponent).join("/")}`);
  target.search = request.nextUrl.search;
  const headers = new Headers(forwardedHeaders(request.headers));
  for (const name of FORWARDED_REQUEST_HEADERS) {
    const value = request.headers.get(name);
    if (value) {
      headers.set(name, value);
    }
  }
  if (accessToken) {
    headers.set("authorization", `Bearer ${accessToken}`);
  }
  return fetch(target, {
    method: request.method,
    headers,
    body: body && body.byteLength > 0 ? body : undefined,
    cache: "no-store",
    redirect: "manual",
  });
}

async function proxy(request: NextRequest, context: { params: Promise<{ path: string[] }> }): Promise<NextResponse> {
  const { path } = await context.params;
  const jar = await cookies();
  let accessToken = jar.get(ACCESS_COOKIE)?.value ?? null;
  const refreshToken = jar.get(REFRESH_COOKIE)?.value ?? null;
  const body = request.method === "GET" || request.method === "HEAD" ? null : await request.arrayBuffer();

  let upstream: Response;
  try {
    upstream = await forward(request, path, accessToken, body);
  } catch {
    return NextResponse.json({ title: "The API could not be reached." }, { status: 502 });
  }

  let refreshed: Parameters<typeof setAuthCookies>[1] | null = null;
  if (upstream.status === 401 && refreshToken) {
    try {
      refreshed = await authApi.refresh(refreshToken);
      accessToken = refreshed.accessToken;
      upstream = await forward(request, path, accessToken, body);
    } catch (error) {
      const response = NextResponse.json({ title: "Session expired." }, { status: 401 });
      if (!(error instanceof ApiError) || error.problem.status === 401) {
        clearAuthCookies(response.cookies);
      }
      return response;
    }
  }

  const headers = new Headers();
  for (const name of FORWARDED_RESPONSE_HEADERS) {
    const value = upstream.headers.get(name);
    if (value) {
      headers.set(name, value);
    }
  }
  const response = new NextResponse(upstream.status === 204 ? null : upstream.body, { status: upstream.status, headers });
  if (refreshed) {
    setAuthCookies(response.cookies, refreshed);
  }
  return response;
}

export const GET = proxy;
export const POST = proxy;
export const PUT = proxy;
export const PATCH = proxy;
export const DELETE = proxy;
