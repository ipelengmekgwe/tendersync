import { DisclaimerBanner } from "@/components/disclaimer-banner";
import { TendersView } from "@/components/tenders/tenders-view";
import { parseTenderFilters, type TenderSearchParams } from "@/components/tenders/filters";

export const metadata = { title: "Tenders" };

/** WP13-lite listing: the full list with search, filters, sort and paging, all held in the URL so views can be shared. */
export default async function TendersPage({ searchParams }: { searchParams: Promise<TenderSearchParams> }) {
  const filters = parseTenderFilters(await searchParams);
  return (
    <div className="space-y-4">
      <div>
        <h1 className="text-2xl font-semibold tracking-tight">Tenders</h1>
        <p className="text-sm text-muted-foreground">Everything ingested from eTenders. Closing times are South African time.</p>
      </div>
      <DisclaimerBanner />
      <TendersView filters={filters} />
    </div>
  );
}
