import { describe, expect, it } from "vitest";
import { isCrossSiteWrite } from "@/lib/auth/csrf";

const headers = (values: Record<string, string>) => new Headers(values);

describe("isCrossSiteWrite", () => {
  it("lets reads through whatever their origin", () => {
    expect(isCrossSiteWrite("GET", headers({ "sec-fetch-site": "cross-site" }))).toBe(false);
  });

  it("blocks posts another site makes the browser send", () => {
    expect(isCrossSiteWrite("POST", headers({ "sec-fetch-site": "cross-site" }))).toBe(true);
    expect(isCrossSiteWrite("POST", headers({ "sec-fetch-site": "same-site" }))).toBe(true);
    expect(isCrossSiteWrite("POST", headers({ origin: "https://evil.example", host: "tendersync.example" }))).toBe(true);
  });

  it("allows the dashboard's own requests and non-browser clients", () => {
    expect(isCrossSiteWrite("POST", headers({ "sec-fetch-site": "same-origin" }))).toBe(false);
    expect(isCrossSiteWrite("PATCH", headers({ origin: "https://tendersync.example", host: "tendersync.example" }))).toBe(false);
    expect(isCrossSiteWrite("POST", headers({ origin: "https://tendersync.example", "x-forwarded-host": "tendersync.example", host: "10.0.0.5:3000" }))).toBe(false);
    expect(isCrossSiteWrite("DELETE", headers({}))).toBe(false);
  });
});
