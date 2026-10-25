import { SubNav } from "@/components/sub-nav";
import { getSession, hasRole } from "@/lib/auth/session";

/** Settings (PLAN.md §11): notification channels for everyone, interest categories for Admins. */
export default async function SettingsLayout({ children }: { children: React.ReactNode }) {
  const session = await getSession();
  const items = [{ href: "/settings/notifications", label: "Notifications" }];
  if (session && hasRole(session.user, "Admin")) {
    items.push({ href: "/settings/categories", label: "Interest categories" });
  }
  return (
    <div className="space-y-4">
      <h1 className="text-2xl font-semibold tracking-tight">Settings</h1>
      <SubNav label="Settings sections" items={items} />
      {children}
    </div>
  );
}
