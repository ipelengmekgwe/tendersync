import { UsersManager } from "@/components/admin/users-manager";
import { getSession } from "@/lib/auth/session";

export const metadata = { title: "Users" };

export default async function UsersPage() {
  const session = await getSession();
  return <UsersManager currentUserId={session?.user.id ?? null} />;
}
