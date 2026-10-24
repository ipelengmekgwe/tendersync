import type { ResponseCookies } from "next/dist/compiled/@edge-runtime/cookies";
import { config } from "@/lib/config";
import type { components } from "@/lib/api/schema";

export type AuthResponse = components["schemas"]["AuthResponse"];
export type User = components["schemas"]["UserDto"];

/** Cookie names. All three are httpOnly: the browser never reads a token, it only carries them to our route handlers. */
export const ACCESS_COOKIE = "ts_access";
export const REFRESH_COOKIE = "ts_refresh";
export const USER_COOKIE = "ts_user";

type CookieJar = Pick<ResponseCookies, "set" | "delete">;

function secondsUntil(iso: string): number {
  return Math.max(1, Math.floor((new Date(iso).getTime() - Date.now()) / 1000));
}

/** Stores the API's tokens and the user summary from a login or refresh response. */
export function setAuthCookies(jar: CookieJar, auth: AuthResponse): void {
  const base = { httpOnly: true, sameSite: "lax" as const, secure: config.secureCookies, path: "/" };
  jar.set(ACCESS_COOKIE, auth.accessToken, { ...base, maxAge: secondsUntil(auth.accessTokenExpiresAt) });
  jar.set(REFRESH_COOKIE, auth.refreshToken, { ...base, maxAge: secondsUntil(auth.refreshTokenExpiresAt) });
  jar.set(USER_COOKIE, JSON.stringify(auth.user), { ...base, maxAge: secondsUntil(auth.refreshTokenExpiresAt) });
}

export function clearAuthCookies(jar: CookieJar): void {
  for (const name of [ACCESS_COOKIE, REFRESH_COOKIE, USER_COOKIE]) {
    jar.delete(name);
  }
}

/** Admin ⊃ Reviewer ⊃ Viewer, as the API's RoleHierarchy. Safe for client components (no server imports here). */
export function hasRole(user: User, role: "Admin" | "Reviewer" | "Viewer"): boolean {
  const rank = { Viewer: 0, Reviewer: 1, Admin: 2 } as const;
  return user.roles.some((r) => (rank as Record<string, number>)[r] >= rank[role]);
}

export function parseUserCookie(value: string | undefined): User | null {
  if (!value) {
    return null;
  }
  try {
    const user = JSON.parse(value) as User;
    return typeof user.email === "string" && Array.isArray(user.roles) ? user : null;
  } catch {
    return null;
  }
}
