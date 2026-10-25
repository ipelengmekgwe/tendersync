import Link from "next/link";
import { notFound } from "next/navigation";
import { ArrowLeft, Download, ExternalLink } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { DisclaimerBanner } from "@/components/disclaimer-banner";
import { PdfViewerLoader } from "@/components/documents/pdf-viewer-loader";
import { serverApi } from "@/lib/api/server";
import { documentDownloadHref, isPdf, parsePage } from "@/lib/documents";
import { DocumentStatus, label } from "@/lib/enums";

export const metadata = { title: "Document" };

type SearchParams = Record<string, string | string[] | undefined>;

/**
 * A tender document opened at a page (PLAN.md §11: "click to open the PDF at the page"). PDFs render in the viewer;
 * other formats show the extracted text per page, with the requested page first.
 */
export default async function DocumentPage({ params, searchParams }: { params: Promise<{ id: string; docId: string }>; searchParams: Promise<SearchParams> }) {
  const { id, docId } = await params;
  const query = await searchParams;
  const page = parsePage(query.page);
  const quoteParam = Array.isArray(query.quote) ? query.quote[0] : query.quote;
  const quote = quoteParam?.trim() ? quoteParam.trim() : null;

  const client = await serverApi();
  const { data: tender, response } = await client.GET("/api/v1/tenders/{id}", { params: { path: { id } } });
  if (response.status === 404 || !tender) {
    notFound();
  }
  const document = tender.documents.find((d) => d.id === docId);
  if (!document) {
    notFound();
  }

  const pdf = isPdf(document.format, document.title);
  const text = document.stored && !pdf ? (await client.GET("/api/v1/tenders/{id}/documents/{docId}/text", { params: { path: { id, docId } } })).data : null;
  const pages = text ? [...text.pages].sort((a, b) => (a.page === page ? -1 : b.page === page ? 1 : a.page - b.page)) : [];

  return (
    <div className="space-y-4">
      <Link href={`/tenders/${id}#requirements`} className="inline-flex items-center gap-1 text-sm text-muted-foreground hover:text-foreground">
        <ArrowLeft className="size-4" aria-hidden /> {tender.bidNumber}
      </Link>
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="min-w-0">
          <h1 className="text-xl font-semibold tracking-tight break-words">{document.title}</h1>
          <p className="text-sm text-muted-foreground">{tender.title}</p>
        </div>
        <div className="flex flex-wrap items-center gap-3 text-sm">
          <Badge variant="outline">{label(DocumentStatus, document.status)}</Badge>
          {document.stored ? (
            <a href={documentDownloadHref(id, docId)} className="inline-flex items-center gap-1 text-primary hover:underline">
              <Download className="size-4" aria-hidden /> Download
            </a>
          ) : null}
          <a href={document.url} target="_blank" rel="noreferrer" className="inline-flex items-center gap-1 text-muted-foreground hover:underline">
            <ExternalLink className="size-4" aria-hidden /> Original
            <span className="sr-only">(opens eTenders in a new tab)</span>
          </a>
        </div>
      </div>
      <DisclaimerBanner />

      {quote ? (
        <figure className="rounded-md border-l-4 border-primary bg-muted/40 px-3 py-2 text-sm" data-testid="source-quote">
          <figcaption className="text-xs text-muted-foreground">Source quote, page {page}</figcaption>
          <blockquote className="italic">{quote}</blockquote>
        </figure>
      ) : null}

      {!document.stored ? (
        <p className="rounded-md border border-dashed p-6 text-sm text-muted-foreground">
          This document has not been downloaded yet. Use &ldquo;Download documents&rdquo; on the tender page, or open the original on eTenders.
        </p>
      ) : pdf ? (
        <PdfViewerLoader url={documentDownloadHref(id, docId)} initialPage={page} quote={quote} />
      ) : pages.length === 0 ? (
        <p className="rounded-md border border-dashed p-6 text-sm text-muted-foreground">No extracted text for this document yet. Download it to read it.</p>
      ) : (
        <div className="space-y-4">
          {pages.map((p) => (
            <section key={p.page} id={`page-${p.page}`} className="rounded-md border p-4">
              <h2 className="mb-2 text-sm font-medium">{p.page === 0 ? "Text" : `Page ${p.page}`}</h2>
              <pre className="font-sans text-sm whitespace-pre-wrap">{p.text}</pre>
            </section>
          ))}
        </div>
      )}
    </div>
  );
}
