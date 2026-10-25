import { describe, expect, it } from "vitest";
import { parseSavedViews, removeView, saveView } from "@/components/tenders/saved-views";
import { historyEntries } from "@/components/tenders/history-card";

describe("saved views", () => {
  it("survives junk in storage", () => {
    expect(parseSavedViews(null)).toEqual([]);
    expect(parseSavedViews("not json")).toEqual([]);
    expect(parseSavedViews('{"name":"x"}')).toEqual([]);
    expect(parseSavedViews('[{"name":"IT closing soon","query":"?closingWithin=7"},{"name":""},{"query":"x"}]')).toEqual([
      { name: "IT closing soon", query: "closingWithin=7" },
    ]);
  });

  it("puts the newest first and replaces a view with the same name", () => {
    let views = saveView([], "Gauteng", "province=3");
    views = saveView(views, "Matched", "matchedOnly=true");
    views = saveView(views, "gauteng", "province=3&closingWithin=14");

    expect(views).toEqual([
      { name: "gauteng", query: "province=3&closingWithin=14" },
      { name: "Matched", query: "matchedOnly=true" },
    ]);
    expect(removeView(views, "Matched")).toEqual([{ name: "gauteng", query: "province=3&closingWithin=14" }]);
    expect(saveView(views, "   ", "q=x")).toBe(views);
  });
});

describe("tender history", () => {
  it("merges status changes, releases and notifications newest first", () => {
    const entries = historyEntries({
      tenderId: "t",
      status: 2,
      statusReason: null,
      firstSeenAt: "2026-10-01T08:00:00Z",
      lastSeenAt: "2026-10-20T08:00:00Z",
      lastCheckedAt: null,
      statusHistory: [{ from: 1, to: 2, reason: null, at: "2026-10-20T08:00:00Z" }],
      releases: [{ id: "r", releaseId: "ocds-1-r1", releaseDate: "2026-10-01T07:00:00Z", tag: "tender", seenAt: "2026-10-01T08:00:00Z" }],
      notifications: [{ id: "n", type: 0, channel: 1, recipient: "12***", status: 1, sentAt: "2026-10-02T08:00:00Z", createdAt: "2026-10-02T07:59:00Z" }],
    });

    expect(entries.map((e) => e.kind)).toEqual(["Status", "Notification", "Release"]);
    expect(entries[0].text).toBe("Open → Closing soon");
    expect(entries[1].text).toBe("New match by Telegram to 12***: sent");
  });
});
