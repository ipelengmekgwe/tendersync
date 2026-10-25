import { CompanyDocuments } from "@/components/company/company-documents";
import { getSession, hasRole } from "@/lib/auth/session";
import { loadActiveProfile } from "@/lib/company";

export const metadata = { title: "Company documents" };

/** The encrypted document library (PLAN.md §11, §12): upload, expiry badges, download; Admins delete. */
export default async function CompanyDocumentsPage() {
  const [profile, session] = await Promise.all([loadActiveProfile(), getSession()]);
  if (!profile) {
    return <p className="rounded-md border border-dashed p-6 text-sm text-muted-foreground">Create the company profile first; documents belong to it.</p>;
  }
  return <CompanyDocuments profileId={profile.id} canDelete={session ? hasRole(session.user, "Admin") : false} />;
}
