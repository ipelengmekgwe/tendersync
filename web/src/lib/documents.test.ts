import { describe, expect, it } from "vitest";
import { documentDownloadHref, documentViewerHref, formatBytes, isPdf, parsePage } from "@/lib/documents";
import { highlightQuote } from "@/lib/highlight";

describe("document links", () => {
  it("opens the viewer at the source page with the quote", () => {
    expect(documentViewerHref("t1", "d1", 3, "Bidders must be registered (CSD).")).toBe(
      "/tenders/t1/documents/d1?page=3&quote=Bidders+must+be+registered+%28CSD%29.",
    );
    expect(documentViewerHref("t1", "d1")).toBe("/tenders/t1/documents/d1");
    expect(documentViewerHref("t1", "d1", 0, "")).toBe("/tenders/t1/documents/d1");
  });

  it("downloads through the authenticated proxy", () => {
    expect(documentDownloadHref("t1", "d1")).toBe("/api/proxy/tenders/t1/documents/d1/download");
  });

  it("accepts only positive whole page numbers from the address bar", () => {
    expect(parsePage("4")).toBe(4);
    expect(parsePage(["2", "9"])).toBe(2);
    for (const bad of ["0", "-1", "2.5", "x", undefined]) {
      expect(parsePage(bad)).toBe(1);
    }
  });

  it("recognises PDFs by format or file name and formats sizes", () => {
    expect(isPdf("pdf")).toBe(true);
    expect(isPdf(null, "Bid Document.PDF")).toBe(true);
    expect(isPdf("docx", "Bid.docx")).toBe(false);
    expect(formatBytes(3_210)).toBe("3 KB");
    expect(formatBytes(5_400_000)).toBe("5.1 MB");
    expect(formatBytes(null)).toBe("");
  });
});

describe("quote highlighting", () => {
  const quote = "Bidders must be registered on the Central Supplier Database (CSD).";

  it("marks text-layer items that are part of the quote", () => {
    expect(highlightQuote("2.1 Bidders must be registered on the Central Supplier Database (CSD).", quote)).toBe(
      "<mark>2.1 Bidders must be registered on the Central Supplier Database (CSD).</mark>",
    );
    expect(highlightQuote("Central Supplier Database", quote)).toBe("<mark>Central Supplier Database</mark>");
  });

  it("leaves other text alone, skips tiny fragments and escapes HTML", () => {
    expect(highlightQuote("Failure to comply", quote)).toBe("Failure to comply");
    expect(highlightQuote("the", quote)).toBe("the");
    expect(highlightQuote("<b>&</b>", null)).toBe("&lt;b&gt;&amp;&lt;/b&gt;");
  });
});
