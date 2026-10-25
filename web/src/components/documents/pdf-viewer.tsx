"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import { Document, Page, pdfjs } from "react-pdf";
import "react-pdf/dist/Page/AnnotationLayer.css";
import "react-pdf/dist/Page/TextLayer.css";
import { ChevronLeft, ChevronRight, Minus, Plus } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { highlightQuote } from "@/lib/highlight";

// react-pdf: the worker must be configured in the module that renders <Document>.
pdfjs.GlobalWorkerOptions.workerSrc = new URL("pdfjs-dist/build/pdf.worker.min.mjs", import.meta.url).toString();

/**
 * PLAN.md §11 viewer: renders the stored PDF through the proxy, opens at the requested page, keeps the page in the
 * address bar (so a requirement link can be shared) and marks the words of the source quote in the text layer.
 */
export function PdfViewer({ url, initialPage, quote }: { url: string; initialPage: number; quote: string | null }) {
  const [numPages, setNumPages] = useState<number | null>(null);
  const [page, setPage] = useState(initialPage);
  const [pageInput, setPageInput] = useState(String(initialPage));
  const [scale, setScale] = useState(1);
  const [width, setWidth] = useState<number>(800);
  const container = useRef<HTMLDivElement>(null);

  useEffect(() => {
    const element = container.current;
    if (!element) {
      return;
    }
    const observer = new ResizeObserver(([entry]) => setWidth(Math.min(entry.contentRect.width, 1000)));
    observer.observe(element);
    return () => observer.disconnect();
  }, []);

  const goTo = useCallback(
    (next: number) => {
      const bounded = Math.max(1, numPages ? Math.min(next, numPages) : next);
      setPage(bounded);
      setPageInput(String(bounded));
      const params = new URLSearchParams(window.location.search);
      params.set("page", String(bounded));
      window.history.replaceState(null, "", `${window.location.pathname}?${params.toString()}`);
    },
    [numPages],
  );

  const textRenderer = useCallback(({ str }: { str: string }) => highlightQuote(str, quote), [quote]);

  return (
    <div className="space-y-3">
      <div className="flex flex-wrap items-center gap-2 text-sm">
        <Button variant="outline" size="icon-sm" aria-label="Previous page" disabled={page <= 1} onClick={() => goTo(page - 1)}>
          <ChevronLeft />
        </Button>
        <form
          className="flex items-center gap-2"
          onSubmit={(e) => {
            e.preventDefault();
            const n = Number(pageInput);
            if (Number.isInteger(n)) {
              goTo(n);
            }
          }}
        >
          <Label htmlFor="pdf-page" className="sr-only">
            Page number
          </Label>
          <Input id="pdf-page" className="h-8 w-16 text-center" inputMode="numeric" value={pageInput} onChange={(e) => setPageInput(e.target.value)} />
        </form>
        <span aria-live="polite" data-testid="pdf-page-indicator">
          Page {page} of {numPages ?? "…"}
        </span>
        <Button variant="outline" size="icon-sm" aria-label="Next page" disabled={numPages !== null && page >= numPages} onClick={() => goTo(page + 1)}>
          <ChevronRight />
        </Button>
        <span className="ml-auto flex items-center gap-1">
          <Button variant="outline" size="icon-sm" aria-label="Zoom out" disabled={scale <= 0.5} onClick={() => setScale((s) => Math.round((s - 0.25) * 100) / 100)}>
            <Minus />
          </Button>
          <span className="w-12 text-center tabular-nums">{Math.round(scale * 100)}%</span>
          <Button variant="outline" size="icon-sm" aria-label="Zoom in" disabled={scale >= 3} onClick={() => setScale((s) => Math.round((s + 0.25) * 100) / 100)}>
            <Plus />
          </Button>
        </span>
      </div>

      <div ref={container} className="overflow-auto rounded-md border bg-muted/40 p-2">
        <Document
          file={url}
          suspense={false}
          loading={<p className="p-6 text-sm text-muted-foreground">Loading the document…</p>}
          error={
            <p role="alert" className="p-6 text-sm text-destructive">
              The PDF could not be shown. Download it instead.
            </p>
          }
          onLoadSuccess={(pdf) => {
            setNumPages(pdf.numPages);
            if (page > pdf.numPages) {
              goTo(pdf.numPages);
            }
          }}
        >
          <Page
            pageNumber={page}
            width={Math.max(width - 16, 200)}
            scale={scale}
            customTextRenderer={quote ? textRenderer : undefined}
            className="mx-auto w-fit shadow-sm"
            loading={<p className="p-6 text-sm text-muted-foreground">Rendering page {page}…</p>}
          />
        </Document>
      </div>
    </div>
  );
}
