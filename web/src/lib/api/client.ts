import createClient from "openapi-fetch";
import type { paths } from "@/lib/api/schema";

/**
 * The browser-side client, typed from the API's OpenAPI document (npm run generate:api). Calls go through /api/proxy,
 * which adds the bearer token from the httpOnly cookie, so paths keep their /api/v1 prefix as the schema declares them.
 */
export const api = createClient<paths>({
  baseUrl: "/api/proxy",
  fetch: async (input: Request) => {
    // Rewrite {origin}/api/proxy/api/v1/... to {origin}/api/proxy/... : the proxy maps onto the API's /api/v1.
    const url = new URL(input.url);
    url.pathname = url.pathname.replace(/^\/api\/proxy\/api\/v1\//, "/api/proxy/");
    // Buffer the body: copying the Request would send it as a stream, which Chrome only allows over HTTP/2
    // (PATCH/POST failed with ERR_ALPN_NEGOTIATION_FAILED against the HTTP/1.1 dev server).
    const body = input.method === "GET" || input.method === "HEAD" ? undefined : await input.arrayBuffer();
    return fetch(url, { method: input.method, headers: input.headers, body, signal: input.signal, credentials: "same-origin" });
  },
});

export type ApiPaths = paths;
