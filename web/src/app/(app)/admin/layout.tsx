import { SubNav } from "@/components/sub-nav";
import { requireRole } from "@/lib/auth/require-role";

/** Admin (PLAN.md §11): pipeline health, audit log, users. Other roles get the 404 page; the API enforces the same. */
export default async function AdminLayout({ children }: { children: React.ReactNode }) {
  await requireRole("Admin");
  return (
    <div className="space-y-4">
      <h1 className="text-2xl font-semibold tracking-tight">Admin</h1>
      <SubNav
        label="Admin sections"
        items={[
          { href: "/admin/pipeline", label: "Pipeline" },
          { href: "/admin/audit", label: "Audit log" },
          { href: "/admin/users", label: "Users" },
        ]}
      />
      {children}
    </div>
  );
}
