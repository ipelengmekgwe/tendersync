import type { MatchStatusValue } from "@/lib/enums";

/** The inbox tabs (PLAN.md §11): one per workflow status a person works through. Won/Lost/Expired live under Applied. */
export const INBOX_TABS = [
  { key: "new", label: "New", status: 0 },
  { key: "reviewing", label: "Reviewing", status: 1 },
  { key: "applying", label: "Applying", status: 3 },
  { key: "applied", label: "Applied", status: 4 },
  { key: "dismissed", label: "Dismissed", status: 2 },
] as const satisfies readonly { key: string; label: string; status: MatchStatusValue }[];

export type InboxTab = (typeof INBOX_TABS)[number];
export type InboxTabKey = InboxTab["key"];

export function parseInboxTab(value: string | string[] | undefined): InboxTab {
  const key = Array.isArray(value) ? value[0] : value;
  return INBOX_TABS.find((t) => t.key === key) ?? INBOX_TABS[0];
}

export interface MatchAction {
  /** Status the action moves the match to. */
  to: MatchStatusValue;
  label: string;
  /** Dismissing asks for a reason first. */
  needsReason?: boolean;
}

/**
 * The quick actions offered for a match in each status. These only record a decision: TenderSync never submits a bid
 * (ADR-015), so "Mark applied" means a person submitted it on eTenders themselves.
 */
export function matchActions(status: number): MatchAction[] {
  const dismiss: MatchAction = { to: 2, label: "Dismiss", needsReason: true };
  switch (status) {
    case 0:
      return [{ to: 1, label: "Start review" }, { to: 3, label: "Applying" }, dismiss];
    case 1:
      return [{ to: 3, label: "Applying" }, { to: 0, label: "Back to new" }, dismiss];
    case 3:
      return [{ to: 4, label: "Mark applied" }, { to: 1, label: "Back to review" }, dismiss];
    case 4:
      return [{ to: 5, label: "Won" }, { to: 6, label: "Lost" }];
    case 2:
    case 5:
    case 6:
    case 7:
      return [{ to: 0, label: "Reopen" }];
    default:
      return [];
  }
}

/** Common reasons offered in the dismiss dialog; free text is always allowed. */
export const DISMISS_REASONS = [
  "Not our line of work",
  "Outside our provinces",
  "Cannot meet a mandatory requirement",
  "Closing too soon to prepare",
  "Contract too large or too small",
] as const;
