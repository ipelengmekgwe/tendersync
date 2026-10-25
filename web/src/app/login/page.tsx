import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { LoginForm } from "@/app/login/login-form";
import { safeNextPath } from "@/lib/auth/redirect";

export const metadata = { title: "Sign in" };

export default async function LoginPage({ searchParams }: { searchParams: Promise<{ next?: string }> }) {
  const { next } = await searchParams;
  return (
    <main className="flex min-h-screen items-center justify-center p-4">
      <Card className="w-full max-w-sm">
        <CardHeader>
          <CardTitle className="text-xl">TenderSync</CardTitle>
          <CardDescription>Sign in with the account an administrator created for you.</CardDescription>
        </CardHeader>
        <CardContent>
          <LoginForm next={safeNextPath(next)} />
        </CardContent>
      </Card>
    </main>
  );
}
