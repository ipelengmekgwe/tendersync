import { cookies } from "next/headers";
import { ACCESS_COOKIE, REFRESH_COOKIE, USER_COOKIE, parseUserCookie, type User } from "@/lib/auth/cookies";

export interface Session {
  user: User;
  accessToken: string | null;
  refreshToken: string | null;
}

/** The signed-in user as recorded at login, read from the httpOnly cookies on the server. Null when signed out. */
export async function getSession(): Promise<Session | null> {
  const jar = await cookies();
  const user = parseUserCookie(jar.get(USER_COOKIE)?.value);
  const refreshToken = jar.get(REFRESH_COOKIE)?.value ?? null;
  if (!user || !refreshToken) {
    return null;
  }
  return { user, accessToken: jar.get(ACCESS_COOKIE)?.value ?? null, refreshToken };
}

export { hasRole } from "@/lib/auth/cookies";
