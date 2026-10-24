import Link from "next/link";
import { notFound } from "next/navigation";
import { Badge } from "@/components/ui/badge";
import { DisclaimerBanner } from "@/components/disclaimer-banner";
import { Province, TenderStatus, label, tenderStatusTone } from "@/lib/enums";
import { formatDateTime } from "@/lib/format";
import { serverApi } from "@/lib/api/server";

export const metadata = { title: "Tender" };

/** WP12 placeholder for the detail page: header and summary only. WP13 adds the cards (Can we bid?, requirements, documents, history). */
export default async function TenderPage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = await params;
  const client = await serverApi();
  const { data, response } = await client.GET("/api/v1/tenders/{id}", { params: { path: { id } } });
  if (response.status === 404 || !data) {
    notFound();
  }
  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-start justify-between gap-2">
        <div>
          <div className="font-mono text-sm text-muted-foreground">{data.bidNumber}</div>
          <h1 className="text-2xl font-semibold tracking-tight">{data.title}</h1>
          <p className="text-sm text-muted-foreground">
            {data.buyerName} · {label(Province, data.province)} · closing {formatDateTime(data.closingAt)}
          </p>
        </div>
        <Badge variant={tenderStatusTone(data.status)}>{label(TenderStatus, data.status)}</Badge>
      </div>
      <DisclaimerBanner />
      {data.summary ? <p className="max-w-3xl text-sm">{data.summary}</p> : <p className="text-sm text-muted-foreground">No summary yet: requirement extraction has not run for this tender.</p>}
      <p className="text-sm">
        <a href={data.sourceUrl} target="_blank" rel="noreferrer" className="text-primary hover:underline">
          Open on eTenders
        </a>
        {" · "}
        <Link href="/tenders" className="text-primary hover:underline">
          Back to the list
        </Link>
      </p>
      <p className="text-xs text-muted-foreground">The full detail page (Can we bid?, requirements with sources, documents, history) arrives with WP13.</p>
    </div>
  );
}
