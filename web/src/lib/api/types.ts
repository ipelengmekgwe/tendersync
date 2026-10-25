import type { components } from "@/lib/api/schema";

/** Short names for the generated response shapes the pages use. */
type Schemas = components["schemas"];

export type TenderDetail = Schemas["TenderDetailDto"];
export type TenderDocument = Schemas["TenderDocumentDto"];
export type TenderRequirements = Schemas["TenderRequirementsDto"];
export type Requirement = Schemas["RequirementDto"];
export type QualificationReport = Schemas["QualificationReportDto"];
export type TenderHistory = Schemas["TenderHistoryDto"];
export type MatchListItem = Schemas["MatchListItemDto"];
export type MatchInbox = Schemas["MatchInboxDto"];
export type Match = Schemas["MatchDto"];
export type Overview = Schemas["OverviewDto"];
