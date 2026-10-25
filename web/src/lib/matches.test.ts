import { describe, expect, it } from "vitest";
import { INBOX_TABS, matchActions, parseInboxTab } from "@/lib/matches";

describe("inbox tabs", () => {
  it("reads the tab from the URL and falls back to New", () => {
    expect(parseInboxTab("reviewing").status).toBe(1);
    expect(parseInboxTab(["dismissed", "new"]).status).toBe(2);
    expect(parseInboxTab("won").key).toBe("new");
    expect(parseInboxTab(undefined).key).toBe("new");
  });

  it("has one tab per status a person works through", () => {
    expect(INBOX_TABS.map((t) => t.label)).toEqual(["New", "Reviewing", "Applying", "Applied", "Dismissed"]);
  });
});

describe("match actions", () => {
  it("offers review, applying and dismiss for a new match, and dismiss asks for a reason", () => {
    const actions = matchActions(0);
    expect(actions.map((a) => a.label)).toEqual(["Start review", "Applying", "Dismiss"]);
    expect(actions.find((a) => a.to === 2)?.needsReason).toBe(true);
  });

  it("only offers the outcome once applied, and reopening once dismissed or closed out", () => {
    expect(matchActions(4).map((a) => a.to)).toEqual([5, 6]);
    for (const status of [2, 5, 6, 7]) {
      expect(matchActions(status)).toEqual([{ to: 0, label: "Reopen" }]);
    }
  });

  it("never offers a move to the status the match is already in", () => {
    for (let status = 0; status <= 7; status++) {
      expect(matchActions(status).some((a) => a.to === status)).toBe(false);
    }
  });
});
