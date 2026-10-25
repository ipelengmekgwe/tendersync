import { SubNav } from "@/components/sub-nav";
import { requireRole } from "@/lib/auth/require-role";

/** Company (PLAN.md §11): the profile the qualification engine checks, and the encrypted document library. Reviewer and up. */
export default async function CompanyLayout({ children }: { children: React.ReactNode }) {
  await requireRole("Reviewer");
  return (
    <div className="space-y-4">
      <h1 className="text-2xl font-semibold tracking-tight">Company</h1>
      <SubNav
        label="Company sections"
        items={[
          { href: "/company/profile", label: "Profile" },
          { href: "/company/documents", label: "Documents" },
        ]}
      />
      {children}
    </div>
  );
}
