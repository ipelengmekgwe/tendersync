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
export type CompanyProfile = Schemas["CompanyProfileDto"];
export type CompanyProfileInput = Schemas["CompanyProfileInput"];
export type CompanyDocument = Schemas["CompanyDocumentDto"];
export type InterestCategory = Schemas["InterestCategoryDto"];
export type PrefilterResult = Schemas["PrefilterResult"];
export type ClassificationOutcome = Schemas["ClassificationOutcome"];
export type NotificationPreference = Schemas["NotificationPreferenceDto"];
export type NotificationPreferenceInput = Schemas["NotificationPreferenceInput"];
export type NotificationItem = Schemas["NotificationDto"];
export type UserAccount = Schemas["UserAccount"];
export type RecurringJob = Schemas["RecurringJobInfo"];
export type JobRun = Schemas["JobRunDto"];
export type SourceHealth = Schemas["SourceHealthDto"];
export type LlmCallsSummary = Schemas["LlmCallsSummaryDto"];
export type AuditLog = Schemas["AuditLogDto"];
export type ApplicationListItem = Schemas["ApplicationListItemDto"];
