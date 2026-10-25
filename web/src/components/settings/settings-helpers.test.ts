import { describe, expect, it } from "vitest";
import { toDrafts, toInputs } from "@/components/settings/notification-settings";
import { toProfileDraft, toProfileInput } from "@/components/company/company-profile-form";
import type { CompanyProfile } from "@/lib/api/types";

describe("notification channel drafts", () => {
  it("shows all five channels and only sends those with an address", () => {
    const drafts = toDrafts([
      { id: "p1", channel: 1, address: "123456", types: [0, 1], enabled: true, quietHours: { start: "21:00", end: "06:00", timeZone: "Africa/Johannesburg" }, updatedAt: "" },
    ]);
    expect(drafts.map((d) => d.channel)).toEqual([0, 1, 2, 3, 4]);
    expect(drafts[1]).toMatchObject({ address: "123456", enabled: true, types: [0, 1], quietStart: "21:00", quietEnd: "06:00" });

    const inputs = toInputs(drafts.map((d) => (d.channel === 0 ? { ...d, address: " me@example.com ", enabled: true, quietStart: "22:00", quietEnd: "" } : d)));
    expect(inputs).toEqual([
      { channel: 0, address: "me@example.com", enabled: true, types: [0, 1, 2, 3, 4, 5, 6, 7], quietHours: null },
      { channel: 1, address: "123456", enabled: true, types: [0, 1], quietHours: { start: "21:00", end: "06:00", timeZone: "Africa/Johannesburg" } },
    ]);
  });
});

describe("company profile form", () => {
  const profile = {
    id: "c1", name: "Demo Technologies (Pty) Ltd", tradingName: null, registrationNumber: "2015/123456/07", vatNumber: null, yearsTrading: 9,
    employeeCount: 24, annualTurnover: 18000000, csdNumber: "MAAA0000001", csdExpiry: "2027-08-23T22:00:00Z", taxPinMasked: "*******000",
    taxPinExpiry: null, bbbeeLevel: 2, bbbeeExpiry: null, bbbeeCertificateType: null, cidbGrade: null, cidbClasses: [], provinces: [3, 10],
    capabilities: { categoryCodes: ["APP_SUPPORT"], technologies: ["Azure"], industries: [], certifications: [], keyPersonnel: [], referenceProjects: [] },
    contactJson: '{"email":"x"}', addressJson: null, isActive: true, shareKeyPersonnelWithModel: false, documentCount: 0, createdAt: "", updatedAt: "",
  } as CompanyProfile;

  it("never wipes the stored tax PIN: untouched sends null, Clear sends an empty string", () => {
    const draft = toProfileDraft(profile);
    expect(draft.taxPin).toBe("");
    expect(toProfileInput(draft).taxPin).toBeNull();
    expect(toProfileInput({ ...draft, taxPin: " NEW123 " }).taxPin).toBe("NEW123");
    expect(toProfileInput({ ...draft, clearTaxPin: true }).taxPin).toBe("");
  });

  it("round-trips the rest of the profile, keeping fields the form does not edit", () => {
    const input = toProfileInput(toProfileDraft(profile));
    expect(input).toMatchObject({
      name: "Demo Technologies (Pty) Ltd",
      yearsTrading: 9,
      csdExpiry: "2027-08-24T00:00:00+02:00",
      provinces: [3, 10],
      contactJson: '{"email":"x"}',
      capabilities: { categoryCodes: ["APP_SUPPORT"], technologies: ["Azure"] },
    });
  });
});
