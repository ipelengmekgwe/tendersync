import { describe, expect, it } from "vitest";
import { forwardedHeaders } from "@/lib/api/forwarded";

describe("forwardedHeaders", () => {
  it("passes the incoming chain on unchanged", () => {
    expect(forwardedHeaders(new Headers({ "x-forwarded-for": "198.51.100.1, 203.0.113.7" }))).toEqual({ "x-forwarded-for": "198.51.100.1, 203.0.113.7" });
  });

  it("adds nothing when there is no chain", () => {
    expect(forwardedHeaders(new Headers())).toEqual({});
    expect(forwardedHeaders(undefined)).toEqual({});
  });
});
