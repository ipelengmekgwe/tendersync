import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { TenderTable, type TenderListItem } from "@/components/tenders/tender-table";

const item: TenderListItem = {
  id: "01a16e25-86a8-7258-a729-e1951acbbdca",
  ocid: "ocds-9t57fa-175445",
  bidNumber: "WCGHCC0038/1/2025",
  title: "Supply of comfort packs",
  buyerName: "Western Cape - Health",
  province: 9,
  mainCategory: 1,
  procurementMethodDetails: "Request for Quotation",
  publishedAt: "2026-10-23T08:00:00Z",
  closingAt: "2026-11-20T09:00:00Z",
  status: 1,
  relevance: 82,
  categoryCodes: ["SW_DEV"],
  matchStatus: 0,
  documentCount: 2,
};

describe("TenderTable", () => {
  it("renders labels for the numeric enums and the closing date in SAST", () => {
    render(<TenderTable items={[item]} now={new Date("2026-11-10T08:00:00Z")} />);

    expect(screen.getByRole("link", { name: "WCGHCC0038/1/2025" })).toHaveAttribute("href", `/tenders/${item.id}`);
    expect(screen.getByText("Western Cape")).toBeInTheDocument();
    expect(screen.getByText("Open")).toBeInTheDocument();
    expect(screen.getByText("New")).toBeInTheDocument();
    expect(screen.getByText("Fri, 20 Nov 2026, 11:00")).toBeInTheDocument();
    expect(screen.getByText("in 10 days")).toBeInTheDocument();
    expect(screen.getByText("82")).toBeInTheDocument();
  });

  it("says so when nothing matches", () => {
    render(<TenderTable items={[]} />);

    expect(screen.getByText("No tenders match these filters.")).toBeInTheDocument();
  });
});
