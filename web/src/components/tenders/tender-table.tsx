import Link from "next/link";
import { Badge } from "@/components/ui/badge";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import type { components } from "@/lib/api/schema";
import { MatchStatus, Province, TenderStatus, label, tenderStatusTone } from "@/lib/enums";
import { closingIn, formatDateTime } from "@/lib/format";

export type TenderListItem = components["schemas"]["TenderListItemDto"];

export function TenderTable({ items, now }: { items: TenderListItem[]; now?: Date }) {
  if (items.length === 0) {
    return <p className="rounded-md border p-6 text-center text-sm text-muted-foreground">No tenders match these filters.</p>;
  }
  return (
    <div className="overflow-x-auto rounded-md border">
      <Table>
        <TableHeader>
          <TableRow>
            <TableHead>Bid number</TableHead>
            <TableHead className="min-w-64">Title</TableHead>
            <TableHead>Buyer</TableHead>
            <TableHead>Closing</TableHead>
            <TableHead>Status</TableHead>
            <TableHead className="text-right">Relevance</TableHead>
            <TableHead>Match</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          {items.map((t) => {
            const relative = closingIn(t.closingAt, now);
            return (
              <TableRow key={t.id}>
                <TableCell className="font-mono text-xs">
                  <Link href={`/tenders/${t.id}`} className="hover:underline">
                    {t.bidNumber}
                  </Link>
                </TableCell>
                <TableCell>
                  <div className="line-clamp-2" title={t.title}>
                    {t.title}
                  </div>
                  {t.categoryCodes.length > 0 ? <div className="mt-1 text-xs text-muted-foreground">{t.categoryCodes.join(", ")}</div> : null}
                </TableCell>
                <TableCell>
                  <div>{t.buyerName}</div>
                  <div className="text-xs text-muted-foreground">{label(Province, t.province)}</div>
                </TableCell>
                <TableCell className="whitespace-nowrap">
                  <div>{formatDateTime(t.closingAt)}</div>
                  {relative ? <div className="text-xs text-muted-foreground">{relative}</div> : null}
                </TableCell>
                <TableCell>
                  <Badge variant={tenderStatusTone(t.status)}>{label(TenderStatus, t.status)}</Badge>
                </TableCell>
                <TableCell className="text-right tabular-nums">{t.relevance ?? "—"}</TableCell>
                <TableCell>{t.matchStatus === null || t.matchStatus === undefined ? "—" : label(MatchStatus, t.matchStatus)}</TableCell>
              </TableRow>
            );
          })}
        </TableBody>
      </Table>
    </div>
  );
}
