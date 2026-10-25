import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { serverApi } from "@/lib/api/server";
import { requireRole } from "@/lib/auth/require-role";
import { ApplicationStatus, label } from "@/lib/enums";
import { formatDateTime } from "@/lib/format";

export const metadata = { title: "Applications" };

/**
 * Applications (PLAN.md §11, §16): a placeholder until Phase 2 adds drafting and packaging. It already lists whatever the
 * applications table holds, with the status column the later pages build on. Nothing is ever submitted for you (ADR-015).
 */
export default async function ApplicationsPage() {
  await requireRole("Reviewer");
  const client = await serverApi();
  const { data } = await client.GET("/api/v1/applications");
  const items = data ?? [];
  return (
    <div className="space-y-4">
      <h1 className="text-2xl font-semibold tracking-tight">Applications</h1>
      <Card>
        <CardHeader>
          <CardTitle>Coming in Phase 2</CardTitle>
          <CardDescription>
            Drafting proposals, compliance matrices and the bid pack arrives in Phase 2. Bids are always reviewed and submitted on eTenders by a person; TenderSync
            never submits one.
          </CardDescription>
        </CardHeader>
        <CardContent>
          <div className="overflow-x-auto">
            <table className="w-full text-left text-sm">
              <thead className="text-xs text-muted-foreground">
                <tr>
                  <th className="py-1 pr-3 font-medium">Tender</th>
                  <th className="py-1 pr-3 font-medium">Company</th>
                  <th className="py-1 pr-3 font-medium">Closing</th>
                  <th className="py-1 font-medium">Status</th>
                </tr>
              </thead>
              <tbody>
                {items.length === 0 ? (
                  <tr className="border-t">
                    <td colSpan={4} className="py-6 text-center text-muted-foreground">
                      No applications yet.
                    </td>
                  </tr>
                ) : (
                  items.map((a) => (
                    <tr key={a.id} className="border-t">
                      <td className="py-2 pr-3">
                        <div className="font-mono text-xs">{a.bidNumber}</div>
                        <div>{a.title}</div>
                      </td>
                      <td className="py-2 pr-3">{a.companyName}</td>
                      <td className="py-2 pr-3 text-xs">{formatDateTime(a.closingAt)}</td>
                      <td className="py-2">{label(ApplicationStatus, a.status)}</td>
                    </tr>
                  ))
                )}
              </tbody>
            </table>
          </div>
        </CardContent>
      </Card>
    </div>
  );
}
