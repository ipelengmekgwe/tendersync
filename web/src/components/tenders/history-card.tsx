import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import type { TenderHistory } from "@/lib/api/types";
import { NotificationChannel, NotificationStatus, NotificationType, TenderStatus, label } from "@/lib/enums";
import { formatDateTime } from "@/lib/format";

interface Entry {
  at: string;
  kind: "Status" | "Release" | "Notification";
  text: string;
}

/** Status changes, releases seen and notifications sent, newest first, as one timeline. */
export function historyEntries(history: TenderHistory): Entry[] {
  const entries: Entry[] = [
    ...history.statusHistory.map((h) => ({
      at: h.at,
      kind: "Status" as const,
      text: `${label(TenderStatus, h.from)} → ${label(TenderStatus, h.to)}${h.reason ? `: ${h.reason}` : ""}`,
    })),
    ...history.releases.map((r) => ({ at: r.seenAt, kind: "Release" as const, text: `Release ${r.releaseId} (${r.tag}) published ${formatDateTime(r.releaseDate)}` })),
    ...history.notifications.map((n) => ({
      at: n.sentAt ?? n.createdAt,
      kind: "Notification" as const,
      text: `${label(NotificationType, n.type)} by ${label(NotificationChannel, n.channel)} to ${n.recipient}: ${label(NotificationStatus, n.status).toLowerCase()}`,
    })),
  ];
  return entries.sort((a, b) => b.at.localeCompare(a.at));
}

export function HistoryCard({ history }: { history: TenderHistory | null }) {
  const entries = history ? historyEntries(history) : [];
  return (
    <Card id="history">
      <CardHeader>
        <CardTitle>History</CardTitle>
        <CardDescription>
          {history
            ? `First seen ${formatDateTime(history.firstSeenAt)} · last checked ${formatDateTime(history.lastCheckedAt ?? history.lastSeenAt)}`
            : "History could not be loaded."}
        </CardDescription>
      </CardHeader>
      {entries.length > 0 ? (
        <CardContent>
          <ol className="space-y-2 border-l pl-4 text-sm">
            {entries.map((e, i) => (
              <li key={`${e.at}-${i}`} className="relative">
                <span className="absolute top-1.5 -left-[1.3rem] size-2 rounded-full bg-muted-foreground/60" aria-hidden />
                <div className="text-xs text-muted-foreground">
                  {formatDateTime(e.at)} · {e.kind}
                </div>
                <div>{e.text}</div>
              </li>
            ))}
          </ol>
        </CardContent>
      ) : null}
    </Card>
  );
}
