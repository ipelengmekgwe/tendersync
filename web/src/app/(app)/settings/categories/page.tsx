import { CategoriesManager } from "@/components/settings/categories-manager";
import { requireRole } from "@/lib/auth/require-role";

export const metadata = { title: "Interest categories" };

/** Interest categories (PLAN.md §11): keywords, negative keywords, weight, and a test against a stored tender. Admin only. */
export default async function CategoriesPage() {
  await requireRole("Admin");
  return <CategoriesManager />;
}
