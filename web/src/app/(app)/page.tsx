import Link from "next/link";
import { ArrowRight } from "lucide-react";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { DisclaimerBanner } from "@/components/disclaimer-banner";
import { getSession } from "@/lib/auth/session";

export const metadata = { title: "Overview" };

/** WP12 shell. The overview tiles (new matches, closing soon, pipeline, cost, expiring documents) arrive with WP13 and WP14. */
export default async function OverviewPage() {
  const session = await getSession();
  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-2xl font-semibold tracking-tight">Welcome, {session?.user.displayName}</h1>
        <p className="text-sm text-muted-foreground">Tenders are ingested every 30 minutes; matched ones are extracted and qualified automatically.</p>
      </div>
      <DisclaimerBanner />
      <div className="grid gap-4 md:grid-cols-2 lg:grid-cols-3">
        <Card>
          <CardHeader>
            <CardTitle>All tenders</CardTitle>
            <CardDescription>Search and filter everything ingested from eTenders.</CardDescription>
          </CardHeader>
          <CardContent>
            <Link href="/tenders" className="inline-flex items-center gap-1 text-sm font-medium text-primary hover:underline">
              Open the list <ArrowRight className="size-4" />
            </Link>
          </CardContent>
        </Card>
        <Card>
          <CardHeader>
            <CardTitle>Matches</CardTitle>
            <CardDescription>Inbox with verdicts and quick actions.</CardDescription>
          </CardHeader>
          <CardContent>
            <Link href="/tenders?matchedOnly=true" className="inline-flex items-center gap-1 text-sm font-medium text-primary hover:underline">
              Matched tenders for now <ArrowRight className="size-4" />
            </Link>
            <p className="mt-2 text-xs text-muted-foreground">The dedicated inbox arrives with WP13.</p>
          </CardContent>
        </Card>
        <Card>
          <CardHeader>
            <CardTitle>API reference</CardTitle>
            <CardDescription>Everything the dashboard shows comes from the versioned API.</CardDescription>
          </CardHeader>
          <CardContent className="text-sm text-muted-foreground">
            Scalar runs on the API host at <code className="font-mono">/scalar</code>.
          </CardContent>
        </Card>
      </div>
    </div>
  );
}
