import Link from "next/link";
import { Download, ExternalLink, Eye } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import type { TenderDocument } from "@/lib/api/types";
import { DocumentStatus, documentStatusTone, label } from "@/lib/enums";
import { documentDownloadHref, documentViewerHref, formatBytes } from "@/lib/documents";

/** The bid pack: download state, extraction state and the needs-OCR flag per document. */
export function DocumentsCard({ tenderId, documents }: { tenderId: string; documents: TenderDocument[] }) {
  return (
    <Card id="documents">
      <CardHeader>
        <CardTitle>Documents</CardTitle>
        <CardDescription>
          {documents.length === 0 ? "The release lists no documents." : `${documents.length} in the bid pack. Stored copies open in the viewer.`}
        </CardDescription>
      </CardHeader>
      {documents.length > 0 ? (
        <CardContent>
          <ul className="divide-y rounded-md border">
            {documents.map((d) => (
              <li key={d.id} className="flex flex-col gap-2 p-3 text-sm sm:flex-row sm:items-center sm:justify-between">
                <div className="min-w-0 space-y-1">
                  <div className="truncate font-medium" title={d.title}>
                    {d.title}
                  </div>
                  <div className="flex flex-wrap items-center gap-1.5 text-xs text-muted-foreground">
                    <Badge variant={documentStatusTone(d.status)}>{label(DocumentStatus, d.status)}</Badge>
                    {d.format ? <span className="uppercase">{d.format}</span> : null}
                    {d.pageCount ? <span>· {d.pageCount} pages</span> : null}
                    {d.sizeBytes ? <span>· {formatBytes(d.sizeBytes)}</span> : null}
                    {d.needsOcr ? <Badge variant="outline">Needs OCR</Badge> : null}
                    {d.error ? <span className="text-destructive">· {d.error}</span> : null}
                  </div>
                </div>
                <div className="flex shrink-0 flex-wrap gap-3 text-sm">
                  {d.stored ? (
                    <>
                      <Link href={documentViewerHref(tenderId, d.id)} className="inline-flex items-center gap-1 text-primary hover:underline">
                        <Eye className="size-4" aria-hidden /> View
                      </Link>
                      <a href={documentDownloadHref(tenderId, d.id)} className="inline-flex items-center gap-1 text-primary hover:underline">
                        <Download className="size-4" aria-hidden /> Download
                      </a>
                    </>
                  ) : null}
                  <a href={d.url} target="_blank" rel="noreferrer" className="inline-flex items-center gap-1 text-muted-foreground hover:underline">
                    <ExternalLink className="size-4" aria-hidden /> Original
                    <span className="sr-only">(opens eTenders in a new tab)</span>
                  </a>
                </div>
              </li>
            ))}
          </ul>
        </CardContent>
      ) : null}
    </Card>
  );
}
