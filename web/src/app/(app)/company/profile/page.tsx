import { CompanyProfileForm } from "@/components/company/company-profile-form";
import { getSession, hasRole } from "@/lib/auth/session";
import { loadActiveProfile } from "@/lib/company";

export const metadata = { title: "Company profile" };

/** The profile editor (PLAN.md §11): details, registrations with expiry, CIDB, provinces, capabilities, references, team. Admins edit; Reviewers read. */
export default async function CompanyProfilePage() {
  const [profile, session] = await Promise.all([loadActiveProfile(), getSession()]);
  return <CompanyProfileForm profile={profile} canEdit={session ? hasRole(session.user, "Admin") : false} />;
}
