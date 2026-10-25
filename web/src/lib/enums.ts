/**
 * The API serialises enums as numbers (System.Text.Json defaults). These maps mirror TenderSync.Domain.Enums; keep
 * them in step when the Domain changes. Names are the labels people see.
 */
export const TenderStatus = {
  0: "Unknown",
  1: "Open",
  2: "Closing soon",
  3: "Closed",
  4: "Awarded",
  5: "Cancelled",
} as const;
export type TenderStatusValue = keyof typeof TenderStatus;

export const Province = {
  0: "Not stated",
  1: "Eastern Cape",
  2: "Free State",
  3: "Gauteng",
  4: "KwaZulu-Natal",
  5: "Limpopo",
  6: "Mpumalanga",
  7: "North West",
  8: "Northern Cape",
  9: "Western Cape",
  10: "National",
} as const;
export type ProvinceValue = keyof typeof Province;

export const ProcurementCategory = { 0: "Not stated", 1: "Goods", 2: "Services", 3: "Works" } as const;

export const MatchStatus = {
  0: "New",
  1: "Reviewing",
  2: "Dismissed",
  3: "Applying",
  4: "Applied",
  5: "Won",
  6: "Lost",
  7: "Expired",
} as const;
export type MatchStatusValue = keyof typeof MatchStatus;

export const TenderSort = { 0: "Closing soonest", 1: "Newest first", 2: "Most relevant" } as const;
export type TenderSortValue = keyof typeof TenderSort;

export function label<T extends Record<number, string>>(map: T, value: number | null | undefined, fallback = "—"): string {
  return value === null || value === undefined ? fallback : (map[value] ?? String(value));
}

/** Tone for status badges. */
export function tenderStatusTone(status: number): "default" | "secondary" | "destructive" | "outline" {
  switch (status) {
    case 2:
      return "destructive";
    case 1:
      return "default";
    case 4:
      return "secondary";
    default:
      return "outline";
  }
}

export const QualificationVerdict = { 0: "Unknown", 1: "Qualify", 2: "Partial", 3: "Does not qualify" } as const;
export type QualificationVerdictValue = keyof typeof QualificationVerdict;

export const FindingOutcome = { 0: "Unknown", 1: "Pass", 2: "Fail", 3: "Expired" } as const;

export const RequirementKind = {
  0: "Mandatory",
  1: "Functional",
  2: "Pricing",
  3: "Document",
  4: "Briefing",
  5: "Submission",
  6: "Evaluation",
  7: "Contract",
  8: "Other",
} as const;

export const RequirementType = {
  0: "Other",
  1: "CSD registration",
  2: "Tax compliance",
  3: "B-BBEE certificate",
  4: "CIDB grade",
  5: "Company registration",
  6: "Insurance",
  7: "Years in business",
  8: "Minimum turnover",
  9: "References",
  10: "Certification",
  11: "Accreditation",
  12: "Local content",
  13: "JV / subcontracting",
} as const;

export const DocumentStatus = { 0: "Pending", 1: "Downloaded", 2: "Failed", 3: "Skipped", 4: "Extracted" } as const;

export const BuyerType = { 0: "Unknown", 1: "National", 2: "Provincial", 3: "Municipal", 4: "State-owned entity", 5: "Other" } as const;

export const SubmissionMethod = { 0: "Not stated", 1: "eTenders online", 2: "Physical", 3: "Email", 4: "Other" } as const;

export const ClassificationStage = { 0: "Not classified", 1: "Keyword", 2: "Model" } as const;

export const NotificationType = {
  0: "New match",
  1: "Qualification ready",
  2: "Closing in 7 days",
  3: "Closing in 3 days",
  4: "Closing tomorrow",
  5: "Briefing reminder",
  6: "Tender changed",
  7: "Award published",
  8: "Pipeline failure",
  9: "Document expiring",
  10: "Daily digest",
  11: "Test",
} as const;

export const NotificationChannel = { 0: "Email", 1: "Telegram", 2: "Teams", 3: "SMS", 4: "WhatsApp" } as const;

export const NotificationStatus = { 0: "Pending", 1: "Sent", 2: "Failed", 3: "Skipped" } as const;

export const ExpiringItemKind = { 0: "Document", 1: "CSD registration", 2: "Tax PIN", 3: "B-BBEE certificate" } as const;

export const MatchSort = { 0: "Closing soonest", 1: "Most relevant", 2: "Newest first" } as const;
export type MatchSortValue = keyof typeof MatchSort;

type Tone = "default" | "secondary" | "destructive" | "outline";

/** Qualify reads as good news, Partial as "look closer", Does not qualify as a stop. */
export function verdictTone(verdict: number | null | undefined): Tone {
  switch (verdict) {
    case 1:
      return "default";
    case 2:
      return "secondary";
    case 3:
      return "destructive";
    default:
      return "outline";
  }
}

export function findingTone(outcome: number): Tone {
  switch (outcome) {
    case 1:
      return "default";
    case 2:
    case 3:
      return "destructive";
    default:
      return "outline";
  }
}

export function matchStatusTone(status: number): Tone {
  switch (status) {
    case 0:
      return "default";
    case 1:
    case 3:
      return "secondary";
    case 2:
    case 6:
    case 7:
      return "outline";
    default:
      return "secondary";
  }
}

export function documentStatusTone(status: number): Tone {
  switch (status) {
    case 4:
      return "default";
    case 1:
      return "secondary";
    case 2:
      return "destructive";
    default:
      return "outline";
  }
}

export const CompanyDocumentType = {
  0: "Other",
  1: "Company registration",
  2: "CSD report",
  3: "Tax PIN",
  4: "B-BBEE certificate",
  5: "Company profile",
  6: "Reference letter",
  7: "CV",
  8: "Certification",
  9: "Pricing template",
  10: "Proposal template",
  11: "SBD 4",
  12: "SBD 6.1",
} as const;

export const ApplicationStatus = { 0: "Draft", 1: "In review", 2: "Approved", 3: "Packaged", 4: "Submitted", 5: "Withdrawn" } as const;

/** Application roles (ADR-011): Admin ⊃ Reviewer ⊃ Viewer. */
export const ROLES = ["Admin", "Reviewer", "Viewer"] as const;
export type Role = (typeof ROLES)[number];
