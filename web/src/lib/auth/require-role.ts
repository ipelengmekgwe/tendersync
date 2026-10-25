import { notFound } from "next/navigation";
import { getSession, hasRole, type Session } from "@/lib/auth/session";

/**
 * Server-side role gate for pages: a signed-in user without the role gets the 404 page, so pages they cannot use look
 * like they do not exist. The API enforces the same roles on every call; this only keeps the UI honest.
 */
export async function requireRole(role: "Admin" | "Reviewer" | "Viewer"): Promise<Session> {
  const session = await getSession();
  if (!session || !hasRole(session.user, role)) {
    notFound();
  }
  return session;
}
