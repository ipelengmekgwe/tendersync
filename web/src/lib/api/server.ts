import { cookies, headers } from "next/headers";
import createClient from "openapi-fetch";
import type { paths } from "@/lib/api/schema";
import { forwardedHeaders } from "@/lib/api/forwarded";
import { ACCESS_COOKIE } from "@/lib/auth/cookies";
import { config } from "@/lib/config";

/**
 * Typed client for server components and route handlers: talks to the API directly with the access token from the
 * cookie. It does not refresh; an expired access token yields a 401 the page treats as "sign in again". Client
 * components use `api` from client.ts, which goes through the refreshing proxy.
 */
export async function serverApi() {
  const token = (await cookies()).get(ACCESS_COOKIE)?.value;
  return createClient<paths>({
    baseUrl: config.apiBaseUrl,
    headers: { ...forwardedHeaders(await headers()), ...(token ? { authorization: `Bearer ${token}` } : {}) },
    cache: "no-store",
  });
}
