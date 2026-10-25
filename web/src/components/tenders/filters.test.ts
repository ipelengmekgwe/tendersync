import { describe, expect, it } from "vitest";
import { parseTenderFilters, toApiQuery, toSearchParams } from "@/components/tenders/filters";

describe("tender filters", () => {
  it("reads the URL, drops out-of-range values and round-trips", () => {
    const filters = parseTenderFilters({ q: " sap ", status: "2", province: "99", sort: "2", page: "3", pageSize: "50", matchedOnly: "true" });

    expect(filters).toEqual({ q: "sap", status: 2, province: null, matchedOnly: true, nearMisses: false, closingWithin: null, sort: 2, page: 3, pageSize: 50 });
    expect(toSearchParams(filters).toString()).toBe("q=sap&status=2&matchedOnly=true&sort=2&page=3&pageSize=50");
    expect(toApiQuery(filters)).toEqual({ ClosingFrom: undefined, ClosingTo: undefined, Q: "sap", Status: 2, Province: undefined, MatchedOnly: true, NearMisses: undefined, Sort: 2, Page: 3, PageSize: 50 });
  });

  it("defaults to closing soonest, page one, 25 per page", () => {
    const filters = parseTenderFilters({});

    expect(filters.sort).toBe(0);
    expect(filters.page).toBe(1);
    expect(filters.pageSize).toBe(25);
    expect(toSearchParams(filters).toString()).toBe("");
  });

  it("turns a closing window into a from/to range starting now", () => {
    const filters = parseTenderFilters({ closingWithin: "7" });
    const now = new Date("2026-10-24T08:00:00Z");

    expect(filters.closingWithin).toBe(7);
    expect(toSearchParams(filters).toString()).toBe("closingWithin=7");
    expect(toApiQuery(filters, now)).toMatchObject({ ClosingFrom: "2026-10-24T08:00:00.000Z", ClosingTo: "2026-10-31T08:00:00.000Z" });
    expect(parseTenderFilters({ closingWithin: "9" }).closingWithin).toBeNull();
  });
});
