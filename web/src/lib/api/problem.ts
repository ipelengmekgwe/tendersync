/** RFC 9457 problem details as the API returns them; validation problems add field errors. */
export interface Problem {
  title?: string | null;
  detail?: string | null;
  status?: number | null;
  errors?: Record<string, string[]> | null;
}

/** The sentence a person should read: field errors first (they say what to fix), then detail, then title. */
export function problemMessage(problem: unknown, status?: number): string {
  const p = (problem ?? {}) as Problem;
  const fieldErrors = p.errors ? Object.values(p.errors).flat().filter(Boolean) : [];
  if (fieldErrors.length > 0) {
    return fieldErrors.join(" ");
  }
  return p.detail || p.title || (status ? `The API responded ${status}.` : "The request failed.");
}

/**
 * Unwraps an openapi-fetch result: returns data, or throws an Error carrying the problem's message. A 204 has no data,
 * which callers that expect nothing treat as success.
 */
export function unwrap<T>(result: { data?: T; error?: unknown; response: Response }): T {
  if (result.error !== undefined || !result.response.ok) {
    throw new Error(problemMessage(result.error, result.response.status));
  }
  return result.data as T;
}
