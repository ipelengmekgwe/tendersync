import { describe, expect, it } from "vitest";
import { safeNextPath } from "@/lib/auth/redirect";

describe("safeNextPath", () => {
  it("keeps same-site paths with their query", () => {
    expect(safeNextPath("/tenders/abc?tab=requirements")).toBe("/tenders/abc?tab=requirements");
  });

  it.each([null, undefined, "", "https://evil.example", "//evil.example", "/\\evil.example", "javascript:alert(1)", "/\tfoo"])(
    "falls back to the overview for %s",
    (value) => {
      expect(safeNextPath(value)).toBe("/");
    },
  );
});
