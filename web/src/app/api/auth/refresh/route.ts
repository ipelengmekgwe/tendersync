import { cookies } from "next/headers";
import { NextResponse } from "next/server";
import { ApiError, authApi } from "@/lib/auth/api-auth";
import { REFRESH_COOKIE, clearAuthCookies, setAuthCookies } from "@/lib/auth/cookies";

/** POST: rotates the refresh token. The proxy does this automatically; the endpoint exists for explicit refreshes. */
export async function POST(): Promise<NextResponse> {
  const refreshToken = (await cookies()).get(REFRESH_COOKIE)?.value;
  if (!refreshToken) {
    return NextResponse.json({ title: "Not signed in." }, { status: 401 });
  }
  try {
    const auth = await authApi.refresh(refreshToken);
    const response = NextResponse.json({ user: auth.user, accessTokenExpiresAt: auth.accessTokenExpiresAt });
    setAuthCookies(response.cookies, auth);
    return response;
  } catch (error) {
    const status = error instanceof ApiError ? error.problem.status : 502;
    const response = NextResponse.json({ title: "Session expired." }, { status });
    if (status === 401) {
      clearAuthCookies(response.cookies);
    }
    return response;
  }
}
