"use client";

import dynamic from "next/dynamic";

/** pdf.js needs the browser (canvas, workers), so the viewer is loaded on the client only. */
export const PdfViewerLoader = dynamic(() => import("@/components/documents/pdf-viewer").then((m) => m.PdfViewer), {
  ssr: false,
  loading: () => <p className="p-6 text-sm text-muted-foreground">Loading the viewer…</p>,
});
