import { describe, expect, it } from "vitest";
import { closingIn, daysUntil, formatDateTime } from "@/lib/format";

describe("format", () => {
  it("shows dates in South African time", () => {
    expect(formatDateTime("2026-11-20T09:00:00Z")).toBe("Fri, 20 Nov 2026, 11:00");
    expect(formatDateTime(null)).toBe("not stated");
  });

  it("counts South African calendar days, not 24-hour spans", () => {
    const lateEvening = new Date("2026-11-20T21:30:00Z"); // 23:30 SAST on the 20th
    expect(daysUntil("2026-11-21T09:00:00Z", lateEvening)).toBe(1);
    const justAfterMidnight = new Date("2026-11-20T22:30:00Z"); // 00:30 SAST on the 21st
    expect(daysUntil("2026-11-21T09:00:00Z", justAfterMidnight)).toBe(0);
  });

  it("describes the closing date relative to now", () => {
    const now = new Date("2026-11-10T08:00:00Z");
    expect(closingIn("2026-11-20T09:00:00Z", now)).toBe("in 10 days");
    expect(closingIn("2026-11-11T09:00:00Z", now)).toBe("tomorrow");
    expect(closingIn("2026-11-10T12:00:00Z", now)).toBe("today");
    expect(closingIn("2026-11-10T07:00:00Z", now)).toBe("closed");
    expect(closingIn(null, now)).toBe("");
  });
});
