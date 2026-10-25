import { DisclaimerBanner } from "@/components/disclaimer-banner";
import { MatchesInbox } from "@/components/matches/matches-inbox";
import { getSession, hasRole } from "@/lib/auth/session";
import type { MatchSortValue } from "@/lib/enums";
import { parseInboxTab } from "@/lib/matches";

export const metadata = { title: "Matches" };

type SearchParams = Record<string, string | string[] | undefined>;

function first(value: string | string[] | undefined): string | undefined {
  return Array.isArray(value) ? value[0] : value;
}

/** The inbox (PLAN.md §11): tabs per workflow status, relevance, verdict, closing countdown and quick actions. */
export default async function MatchesPage({ searchParams }: { searchParams: Promise<SearchParams> }) {
  const params = await searchParams;
  const session = await getSession();
  const sort = Number(first(params.sort));
  const page = Number(first(params.page));
  return (
    <div className="space-y-4">
      <div>
        <h1 className="text-2xl font-semibold tracking-tight">Matches</h1>
        <p className="text-sm text-muted-foreground">
          Tenders that match the company profile. Moving a match only records the team&apos;s decision; bids are always submitted by a person on eTenders.
        </p>
      </div>
      <DisclaimerBanner />
      <MatchesInbox
        canEdit={session ? hasRole(session.user, "Reviewer") : false}
        filters={{
          tab: parseInboxTab(params.tab),
          sort: ([0, 1, 2].includes(sort) ? sort : 0) as MatchSortValue,
          closingSoon: first(params.soon) === "true",
          page: Number.isInteger(page) && page > 0 ? page : 1,
        }}
      />
    </div>
  );
}
