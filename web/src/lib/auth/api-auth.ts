import { config } from "@/lib/config";
import type { AuthResponse } from "@/lib/auth/cookies";

/** Problem details as the API returns them (RFC 9457). */
export interface ApiProblem {
  status: number;
  title: string;
  detail?: string;
  errors?: Record<string, string[]>;
}

export class ApiError extends Error {
  constructor(public readonly problem: ApiProblem) {
    super(problem.detail ?? problem.title);
  }
}

async function readProblem(response: Response): Promise<ApiProblem> {
  try {
    const body = (await response.json()) as Partial<ApiProblem>;
    return { status: response.status, title: body.title ?? response.statusText, detail: body.detail, errors: body.errors };
  } catch {
    return { status: response.status, title: response.statusText || "Request failed" };
  }
}

async function post<T>(path: string, body: unknown, accessToken?: string): Promise<T> {
  const response = await fetch(`${config.apiBaseUrl}/api/v1/auth/${path}`, {
    method: "POST",
    headers: {
      "content-type": "application/json",
      ...(accessToken ? { authorization: `Bearer ${accessToken}` } : {}),
    },
    body: JSON.stringify(body),
    cache: "no-store",
  });
  if (!response.ok) {
    throw new ApiError(await readProblem(response));
  }
  return response.status === 204 ? (undefined as T) : ((await response.json()) as T);
}

export const authApi = {
  login: (email: string, password: string) => post<AuthResponse>("login", { email, password }),
  refresh: (refreshToken: string) => post<AuthResponse>("refresh", { refreshToken }),
  logout: (refreshToken: string, accessToken: string) => post<void>("logout", { refreshToken }, accessToken),
};
