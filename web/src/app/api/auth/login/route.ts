import { NextResponse } from "next/server";
import { ApiError, authApi } from "@/lib/auth/api-auth";
import { setAuthCookies } from "@/lib/auth/cookies";

/** POST { email, password }: exchanges credentials with the API and stores the tokens in httpOnly cookies (ADR-011). */
export async function POST(request: Request): Promise<NextResponse> {
  let body: { email?: string; password?: string };
  try {
    body = (await request.json()) as typeof body;
  } catch {
    return NextResponse.json({ title: "Email and password are required." }, { status: 400 });
  }
  if (!body.email || !body.password) {
    return NextResponse.json({ title: "Email and password are required." }, { status: 400 });
  }

  try {
    const auth = await authApi.login(body.email, body.password);
    const response = NextResponse.json({ user: auth.user });
    setAuthCookies(response.cookies, auth);
    return response;
  } catch (error) {
    if (error instanceof ApiError) {
      return NextResponse.json(error.problem, { status: error.problem.status });
    }
    return NextResponse.json({ title: "The API could not be reached." }, { status: 502 });
  }
}
