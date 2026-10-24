import { redirect } from "next/navigation";
import { AppShell } from "@/components/app-shell";
import { getSession } from "@/lib/auth/session";

/** Every page under the shell needs a session; the middleware redirects first, this is the server-side backstop. */
export default async function AppLayout({ children }: { children: React.ReactNode }) {
  const session = await getSession();
  if (!session) {
    redirect("/login");
  }
  return <AppShell user={session.user}>{children}</AppShell>;
}
