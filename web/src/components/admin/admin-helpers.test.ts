import { describe, expect, it } from "vitest";
import { fillDays, niceMax } from "@/components/admin/cost-chart";
import { latestPerSource, runDuration } from "@/components/admin/pipeline-dashboard";
import { humaniseAction } from "@/components/admin/audit-log";

describe("cost chart data", () => {
  it("fills every South African day in the window, zero where the API had no calls", () => {
    const days = fillDays([{ key: "2026-10-23", costUsd: 0.42, calls: 3, failed: 1 }], "2026-10-21T22:30:00Z", "2026-10-24T09:00:00Z");
    expect(days.map((d) => d.day)).toEqual(["2026-10-22", "2026-10-23", "2026-10-24"]);
    expect(days[1]).toEqual({ day: "2026-10-23", costUsd: 0.42, calls: 3, failed: 1 });
    expect(days[0].costUsd).toBe(0);
  });

  it("rounds the axis to 1, 2 or 5 times a power of ten", () => {
    expect(niceMax(0)).toBe(1);
    expect(niceMax(0.42)).toBe(0.5);
    expect(niceMax(1.3)).toBe(2);
    expect(niceMax(7)).toBe(10);
    expect(niceMax(31)).toBe(50);
  });
});

describe("pipeline helpers", () => {
  it("formats run durations", () => {
    expect(runDuration({ startedAt: "2026-10-24T01:16:00Z", finishedAt: "2026-10-24T01:16:09Z" })).toBe("9s");
    expect(runDuration({ startedAt: "2026-10-24T01:27:17Z", finishedAt: "2026-10-24T01:29:15Z" })).toBe("1m 58s");
    expect(runDuration({ startedAt: "2026-10-24T01:27:17Z", finishedAt: null })).toBe("running");
  });

  it("keeps the newest probe per source and flags drift seen in any recent probe", () => {
    const probe = (source: string, checkedAt: string, ok: boolean, drift: string | null) => ({
      id: checkedAt, source, checkedAt, ok, httpStatus: 200, latencyMs: 100, schemaDriftJson: drift, error: null,
    });
    const latest = latestPerSource([
      probe("eTendersApi", "2026-10-24T01:00:00Z", true, '["tender.newField"]'),
      probe("eTendersApi", "2026-10-24T02:00:00Z", false, null),
      probe("llm-cost-cap", "2026-10-24T01:30:00Z", true, "[]"),
    ]);
    expect(latest.map((s) => [s.source, s.ok, s.drift])).toEqual([
      ["eTendersApi", false, true],
      ["llm-cost-cap", true, false],
    ]);
  });

  it("names audit actions in plain words", () => {
    expect(humaniseAction("UpdateMatchCommand")).toBe("Update match");
    expect(humaniseAction("DownloadCompanyDocumentCommand")).toBe("Download company document");
  });
});
