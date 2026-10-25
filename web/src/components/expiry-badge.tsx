import { Badge } from "@/components/ui/badge";
import { formatDate } from "@/lib/format";
import { expiryState } from "@/lib/lists";

/** Expired (red), expiring within 30 days (amber), valid, or nothing when there is no date. */
export function ExpiryBadge({ expiresAt, now }: { expiresAt: string | null | undefined; now?: Date }) {
  const state = expiryState(expiresAt, now);
  if (state === "none") {
    return <span className="text-xs text-muted-foreground">No expiry</span>;
  }
  if (state === "expired") {
    return <Badge variant="destructive">Expired {formatDate(expiresAt)}</Badge>;
  }
  if (state === "soon") {
    return (
      <Badge variant="outline" className="border-amber-500/60 text-amber-700 dark:text-amber-400">
        Expires {formatDate(expiresAt)}
      </Badge>
    );
  }
  return <Badge variant="secondary">Valid to {formatDate(expiresAt)}</Badge>;
}
