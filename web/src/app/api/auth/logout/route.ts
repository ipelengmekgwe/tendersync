import { cookies } from "next/headers";
import { NextResponse } from "next/server";
import { authApi } from "@/lib/auth/api-auth";
import { ACCESS_COOKIE, REFRESH_COOKIE, clearAuthCookies } from "@/lib/auth/cookies";

/** POST: revokes the refresh token at the API (best effort) and clears the cookies. */
export async function POST(): Promise<NextResponse> {
  const jar = await cookies();
  const refreshToken = jar.get(REFRESH_COOKIE)?.value;
  const accessToken = jar.get(ACCESS_COOKIE)?.value;
  if (refreshToken && accessToken) {
    try {
      await authApi.logout(refreshToken, accessToken);
    } catch {
      // An expired access token or an unreachable API must not keep the user signed in locally.
    }
  }
  const response = NextResponse.json({ ok: true });
  clearAuthCookies(response.cookies);
  return response;
}
