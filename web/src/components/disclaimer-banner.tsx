/** The standing disclaimer (PLAN.md §11, §12): every page that shows tender data carries it. */
export function DisclaimerBanner() {
  return (
    <p className="rounded-md border border-dashed bg-muted/40 px-3 py-2 text-xs text-muted-foreground" role="note">
      Tender data comes from the National Treasury eTenders portal and is processed automatically. Verify every item against
      the source documents before bidding; nothing here is advice and nothing is ever submitted on your behalf.
    </p>
  );
}
