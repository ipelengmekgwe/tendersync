"use client";

import { useRouter } from "next/navigation";
import { useState } from "react";
import { useMutation } from "@tanstack/react-query";
import { Plus, Trash2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import { ExpiryBadge } from "@/components/expiry-badge";
import { api } from "@/lib/api/client";
import { unwrap } from "@/lib/api/problem";
import type { CompanyProfile, CompanyProfileInput } from "@/lib/api/types";
import { Province } from "@/lib/enums";
import { dateInputToIso, formatList, isoToDateInput, parseList } from "@/lib/lists";

interface Cert {
  name: string;
  issuer: string;
  expires: string;
}
interface Person {
  name: string;
  role: string;
  years: string;
  summary: string;
}
interface Reference {
  client: string;
  scope: string;
  value: string;
  year: string;
  contactable: boolean;
}

export interface ProfileDraft {
  name: string;
  tradingName: string;
  registrationNumber: string;
  vatNumber: string;
  yearsTrading: string;
  employeeCount: string;
  annualTurnover: string;
  csdNumber: string;
  csdExpiry: string;
  taxPin: string;
  clearTaxPin: boolean;
  taxPinExpiry: string;
  bbbeeLevel: string;
  bbbeeExpiry: string;
  bbbeeCertificateType: string;
  cidbGrade: string;
  cidbClasses: string;
  provinces: number[];
  categoryCodes: string;
  technologies: string;
  industries: string;
  certifications: Cert[];
  keyPersonnel: Person[];
  references: Reference[];
  shareKeyPersonnelWithModel: boolean;
  contactJson: string | null;
  addressJson: string | null;
}

const str = (v: number | null | undefined) => (v === null || v === undefined ? "" : String(v));
const num = (v: string) => (v.trim() === "" ? null : Number(v));

export function toProfileDraft(p: CompanyProfile | null): ProfileDraft {
  const c = p?.capabilities;
  return {
    name: p?.name ?? "",
    tradingName: p?.tradingName ?? "",
    registrationNumber: p?.registrationNumber ?? "",
    vatNumber: p?.vatNumber ?? "",
    yearsTrading: str(p?.yearsTrading ?? 0),
    employeeCount: str(p?.employeeCount),
    annualTurnover: str(p?.annualTurnover),
    csdNumber: p?.csdNumber ?? "",
    csdExpiry: isoToDateInput(p?.csdExpiry),
    taxPin: "",
    clearTaxPin: false,
    taxPinExpiry: isoToDateInput(p?.taxPinExpiry),
    bbbeeLevel: str(p?.bbbeeLevel),
    bbbeeExpiry: isoToDateInput(p?.bbbeeExpiry),
    bbbeeCertificateType: p?.bbbeeCertificateType ?? "",
    cidbGrade: str(p?.cidbGrade),
    cidbClasses: formatList(p?.cidbClasses),
    provinces: [...(p?.provinces ?? [])],
    categoryCodes: formatList(c?.categoryCodes),
    technologies: formatList(c?.technologies),
    industries: formatList(c?.industries),
    certifications: (c?.certifications ?? []).map((x) => ({ name: x.name, issuer: x.issuer ?? "", expires: isoToDateInput(x.expiresAt) })),
    keyPersonnel: (c?.keyPersonnel ?? []).map((x) => ({ name: x.name, role: x.role, years: str(x.yearsExperience), summary: x.summary ?? "" })),
    references: (c?.referenceProjects ?? []).map((x) => ({ client: x.client, scope: x.scope, value: str(x.value), year: str(x.year), contactable: x.contactable })),
    shareKeyPersonnelWithModel: p?.shareKeyPersonnelWithModel ?? false,
    contactJson: p?.contactJson ?? null,
    addressJson: p?.addressJson ?? null,
  };
}

/** The PUT/POST body. An untouched tax PIN is sent as null (the API keeps the stored one); "Clear" sends an empty string. */
export function toProfileInput(d: ProfileDraft): CompanyProfileInput {
  return {
    name: d.name.trim(),
    tradingName: d.tradingName.trim() || null,
    registrationNumber: d.registrationNumber.trim() || null,
    vatNumber: d.vatNumber.trim() || null,
    yearsTrading: num(d.yearsTrading) ?? 0,
    employeeCount: num(d.employeeCount),
    annualTurnover: num(d.annualTurnover),
    csdNumber: d.csdNumber.trim() || null,
    csdExpiry: dateInputToIso(d.csdExpiry),
    taxPin: d.clearTaxPin ? "" : d.taxPin.trim() || null,
    taxPinExpiry: dateInputToIso(d.taxPinExpiry),
    bbbeeLevel: num(d.bbbeeLevel),
    bbbeeExpiry: dateInputToIso(d.bbbeeExpiry),
    bbbeeCertificateType: d.bbbeeCertificateType.trim() || null,
    cidbGrade: num(d.cidbGrade),
    cidbClasses: parseList(d.cidbClasses).map((x) => x.toUpperCase()),
    provinces: d.provinces,
    capabilities: {
      categoryCodes: parseList(d.categoryCodes).map((x) => x.toUpperCase()),
      technologies: parseList(d.technologies),
      industries: parseList(d.industries),
      certifications: d.certifications.filter((x) => x.name.trim()).map((x) => ({ name: x.name.trim(), issuer: x.issuer.trim() || null, expiresAt: dateInputToIso(x.expires) })),
      keyPersonnel: d.keyPersonnel.filter((x) => x.name.trim()).map((x) => ({ name: x.name.trim(), role: x.role.trim(), yearsExperience: num(x.years), summary: x.summary.trim() || null })),
      referenceProjects: d.references
        .filter((x) => x.client.trim())
        .map((x) => ({ client: x.client.trim(), scope: x.scope.trim(), value: num(x.value), year: num(x.year), contactable: x.contactable })),
    },
    contactJson: d.contactJson,
    addressJson: d.addressJson,
    shareKeyPersonnelWithModel: d.shareKeyPersonnelWithModel,
  };
}

function Field({ id, label, children, hint }: { id: string; label: string; children: React.ReactNode; hint?: React.ReactNode }) {
  return (
    <div className="space-y-1">
      <Label htmlFor={id}>{label}</Label>
      {children}
      {hint ? <div className="text-xs text-muted-foreground">{hint}</div> : null}
    </div>
  );
}

/**
 * /company/profile: everything the qualification engine checks a tender against. Admins edit (the API allows only
 * them); Reviewers see the same form read-only. The tax PIN is never shown, only whether one is stored.
 */
export function CompanyProfileForm({ profile, canEdit }: { profile: CompanyProfile | null; canEdit: boolean }) {
  const router = useRouter();
  const [d, setD] = useState(() => toProfileDraft(profile));
  const set = (patch: Partial<ProfileDraft>) => setD((current) => ({ ...current, ...patch }));

  const save = useMutation({
    mutationFn: async (draft: ProfileDraft) =>
      profile
        ? unwrap(await api.PUT("/api/v1/company-profiles/{id}", { params: { path: { id: profile.id } }, body: toProfileInput(draft) }))
        : unwrap(await api.POST("/api/v1/company-profiles", { body: toProfileInput(draft) })),
    onSuccess: (saved) => {
      setD(toProfileDraft(saved));
      router.refresh();
    },
  });

  if (!profile && !canEdit) {
    return <p className="rounded-md border border-dashed p-6 text-sm text-muted-foreground">No company profile yet. An admin creates it here.</p>;
  }

  return (
    <form
      className="space-y-6"
      onSubmit={(e) => {
        e.preventDefault();
        save.mutate(d);
      }}
    >
      {!canEdit ? (
        <p className="rounded-md border border-dashed bg-muted/40 px-3 py-2 text-sm text-muted-foreground" role="note">
          Read only: an admin edits the company profile. You can upload documents.
        </p>
      ) : null}
      <fieldset disabled={!canEdit} className="space-y-6">
        <Card>
          <CardHeader>
            <CardTitle>Details</CardTitle>
          </CardHeader>
          <CardContent className="grid gap-4 sm:grid-cols-2">
            <Field id="name" label="Registered name">
              <Input id="name" value={d.name} required maxLength={300} onChange={(e) => set({ name: e.target.value })} />
            </Field>
            <Field id="tradingName" label="Trading name">
              <Input id="tradingName" value={d.tradingName} onChange={(e) => set({ tradingName: e.target.value })} />
            </Field>
            <Field id="registrationNumber" label="Registration number">
              <Input id="registrationNumber" value={d.registrationNumber} placeholder="2015/123456/07" onChange={(e) => set({ registrationNumber: e.target.value })} />
            </Field>
            <Field id="vatNumber" label="VAT number">
              <Input id="vatNumber" value={d.vatNumber} onChange={(e) => set({ vatNumber: e.target.value })} />
            </Field>
            <Field id="yearsTrading" label="Years trading">
              <Input id="yearsTrading" type="number" min={0} max={200} value={d.yearsTrading} onChange={(e) => set({ yearsTrading: e.target.value })} />
            </Field>
            <Field id="employeeCount" label="Employees">
              <Input id="employeeCount" type="number" min={0} value={d.employeeCount} onChange={(e) => set({ employeeCount: e.target.value })} />
            </Field>
            <Field id="annualTurnover" label="Annual turnover (ZAR)">
              <Input id="annualTurnover" type="number" min={0} step="1000" value={d.annualTurnover} onChange={(e) => set({ annualTurnover: e.target.value })} />
            </Field>
          </CardContent>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle>Registrations</CardTitle>
            <CardDescription>Expired registrations make mandatory requirements fail; the overview warns 30 days ahead.</CardDescription>
          </CardHeader>
          <CardContent className="grid gap-4 sm:grid-cols-2">
            <Field id="csdNumber" label="CSD supplier number">
              <Input id="csdNumber" value={d.csdNumber} placeholder="MAAA0000000" onChange={(e) => set({ csdNumber: e.target.value })} />
            </Field>
            <Field id="csdExpiry" label="CSD expiry" hint={<ExpiryBadge expiresAt={dateInputToIso(d.csdExpiry)} />}>
              <Input id="csdExpiry" type="date" value={d.csdExpiry} onChange={(e) => set({ csdExpiry: e.target.value })} />
            </Field>
            <Field
              id="taxPin"
              label="Tax compliance PIN"
              hint={
                <span className="flex flex-wrap items-center gap-2">
                  {profile?.taxPinMasked ? `Stored: ${profile.taxPinMasked}. Leave empty to keep it.` : "None stored."}
                  {profile?.taxPinMasked ? (
                    <label className="flex items-center gap-1">
                      <input type="checkbox" checked={d.clearTaxPin} onChange={(e) => set({ clearTaxPin: e.target.checked, taxPin: "" })} />
                      Clear it
                    </label>
                  ) : null}
                </span>
              }
            >
              <Input id="taxPin" value={d.taxPin} autoComplete="off" maxLength={32} disabled={d.clearTaxPin} onChange={(e) => set({ taxPin: e.target.value })} />
            </Field>
            <Field id="taxPinExpiry" label="Tax PIN expiry" hint={<ExpiryBadge expiresAt={dateInputToIso(d.taxPinExpiry)} />}>
              <Input id="taxPinExpiry" type="date" value={d.taxPinExpiry} onChange={(e) => set({ taxPinExpiry: e.target.value })} />
            </Field>
            <Field id="bbbeeLevel" label="B-BBEE level (1–8)">
              <Input id="bbbeeLevel" type="number" min={1} max={8} value={d.bbbeeLevel} onChange={(e) => set({ bbbeeLevel: e.target.value })} />
            </Field>
            <Field id="bbbeeExpiry" label="B-BBEE expiry" hint={<ExpiryBadge expiresAt={dateInputToIso(d.bbbeeExpiry)} />}>
              <Input id="bbbeeExpiry" type="date" value={d.bbbeeExpiry} onChange={(e) => set({ bbbeeExpiry: e.target.value })} />
            </Field>
            <Field id="bbbeeCertificateType" label="B-BBEE evidence">
              <Input id="bbbeeCertificateType" value={d.bbbeeCertificateType} placeholder="Certificate or sworn affidavit" onChange={(e) => set({ bbbeeCertificateType: e.target.value })} />
            </Field>
            <Field id="cidbGrade" label="CIDB grade (1–9)">
              <Input id="cidbGrade" type="number" min={1} max={9} value={d.cidbGrade} onChange={(e) => set({ cidbGrade: e.target.value })} />
            </Field>
            <Field id="cidbClasses" label="CIDB classes" hint="Comma-separated, e.g. EE, SQ">
              <Input id="cidbClasses" value={d.cidbClasses} onChange={(e) => set({ cidbClasses: e.target.value })} />
            </Field>
          </CardContent>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle>Where and what</CardTitle>
          </CardHeader>
          <CardContent className="space-y-4">
            <fieldset className="space-y-2">
              <legend className="text-sm font-medium">Provinces served</legend>
              <div className="grid gap-1 sm:grid-cols-2 lg:grid-cols-3">
                {Object.entries(Province)
                  .filter(([value]) => value !== "0")
                  .map(([value, name]) => (
                    <label key={value} className="flex items-center gap-2 text-sm">
                      <input
                        type="checkbox"
                        checked={d.provinces.includes(Number(value))}
                        onChange={(e) =>
                          set({ provinces: e.target.checked ? [...d.provinces, Number(value)].sort((a, b) => a - b) : d.provinces.filter((p) => p !== Number(value)) })
                        }
                      />
                      {name}
                    </label>
                  ))}
              </div>
            </fieldset>
            <Field id="categoryCodes" label="Interest category codes" hint="Comma-separated codes from Settings → Interest categories. Empty means every category matches.">
              <Input id="categoryCodes" value={d.categoryCodes} placeholder="SW_DEV, APP_SUPPORT" onChange={(e) => set({ categoryCodes: e.target.value })} />
            </Field>
            <Field id="technologies" label="Technologies">
              <Textarea id="technologies" rows={2} value={d.technologies} placeholder=".NET, Azure, SQL Server" onChange={(e) => set({ technologies: e.target.value })} />
            </Field>
            <Field id="industries" label="Industries">
              <Input id="industries" value={d.industries} placeholder="Local government, Health" onChange={(e) => set({ industries: e.target.value })} />
            </Field>
          </CardContent>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle>Certifications</CardTitle>
          </CardHeader>
          <CardContent className="space-y-2">
            {d.certifications.map((c, i) => (
              <div key={i} className="grid gap-2 sm:grid-cols-[1fr_1fr_10rem_auto] sm:items-end">
                <Field id={`cert-${i}-name`} label="Certification">
                  <Input id={`cert-${i}-name`} value={c.name} placeholder="ISO 27001" onChange={(e) => set({ certifications: d.certifications.map((x, j) => (j === i ? { ...x, name: e.target.value } : x)) })} />
                </Field>
                <Field id={`cert-${i}-issuer`} label="Issuer">
                  <Input id={`cert-${i}-issuer`} value={c.issuer} onChange={(e) => set({ certifications: d.certifications.map((x, j) => (j === i ? { ...x, issuer: e.target.value } : x)) })} />
                </Field>
                <Field id={`cert-${i}-expires`} label="Expires">
                  <Input id={`cert-${i}-expires`} type="date" value={c.expires} onChange={(e) => set({ certifications: d.certifications.map((x, j) => (j === i ? { ...x, expires: e.target.value } : x)) })} />
                </Field>
                <Button type="button" variant="ghost" size="icon" aria-label={`Remove certification ${i + 1}`} onClick={() => set({ certifications: d.certifications.filter((_, j) => j !== i) })}>
                  <Trash2 />
                </Button>
              </div>
            ))}
            <Button type="button" variant="outline" size="sm" onClick={() => set({ certifications: [...d.certifications, { name: "", issuer: "", expires: "" }] })}>
              <Plus /> Add certification
            </Button>
          </CardContent>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle>Key personnel</CardTitle>
            <CardDescription>Used to estimate functionality scores.</CardDescription>
          </CardHeader>
          <CardContent className="space-y-3">
            {d.keyPersonnel.map((p, i) => (
              <div key={i} className="grid gap-2 rounded-md border p-2 sm:grid-cols-[1fr_1fr_7rem_auto] sm:items-end">
                <Field id={`person-${i}-name`} label="Name">
                  <Input id={`person-${i}-name`} value={p.name} onChange={(e) => set({ keyPersonnel: d.keyPersonnel.map((x, j) => (j === i ? { ...x, name: e.target.value } : x)) })} />
                </Field>
                <Field id={`person-${i}-role`} label="Role">
                  <Input id={`person-${i}-role`} value={p.role} placeholder="Project manager" onChange={(e) => set({ keyPersonnel: d.keyPersonnel.map((x, j) => (j === i ? { ...x, role: e.target.value } : x)) })} />
                </Field>
                <Field id={`person-${i}-years`} label="Years">
                  <Input id={`person-${i}-years`} type="number" min={0} value={p.years} onChange={(e) => set({ keyPersonnel: d.keyPersonnel.map((x, j) => (j === i ? { ...x, years: e.target.value } : x)) })} />
                </Field>
                <Button type="button" variant="ghost" size="icon" aria-label={`Remove person ${i + 1}`} onClick={() => set({ keyPersonnel: d.keyPersonnel.filter((_, j) => j !== i) })}>
                  <Trash2 />
                </Button>
                <div className="sm:col-span-4">
                  <Field id={`person-${i}-summary`} label="Summary">
                    <Textarea id={`person-${i}-summary`} rows={2} value={p.summary} onChange={(e) => set({ keyPersonnel: d.keyPersonnel.map((x, j) => (j === i ? { ...x, summary: e.target.value } : x)) })} />
                  </Field>
                </div>
              </div>
            ))}
            <Button type="button" variant="outline" size="sm" onClick={() => set({ keyPersonnel: [...d.keyPersonnel, { name: "", role: "", years: "", summary: "" }] })}>
              <Plus /> Add person
            </Button>
            <label className="flex items-start gap-2 text-sm">
              <input type="checkbox" className="mt-1" checked={d.shareKeyPersonnelWithModel} onChange={(e) => set({ shareKeyPersonnelWithModel: e.target.checked })} />
              <span>
                Share key personnel with the model, by role only (never names), when estimating functionality. Off by default for POPIA.
              </span>
            </label>
          </CardContent>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle>Reference projects</CardTitle>
          </CardHeader>
          <CardContent className="space-y-3">
            {d.references.map((r, i) => (
              <div key={i} className="grid gap-2 rounded-md border p-2 sm:grid-cols-[1fr_9rem_6rem_auto_auto] sm:items-end">
                <Field id={`ref-${i}-client`} label="Client">
                  <Input id={`ref-${i}-client`} value={r.client} onChange={(e) => set({ references: d.references.map((x, j) => (j === i ? { ...x, client: e.target.value } : x)) })} />
                </Field>
                <Field id={`ref-${i}-value`} label="Value (ZAR)">
                  <Input id={`ref-${i}-value`} type="number" min={0} value={r.value} onChange={(e) => set({ references: d.references.map((x, j) => (j === i ? { ...x, value: e.target.value } : x)) })} />
                </Field>
                <Field id={`ref-${i}-year`} label="Year">
                  <Input id={`ref-${i}-year`} type="number" min={1990} max={2100} value={r.year} onChange={(e) => set({ references: d.references.map((x, j) => (j === i ? { ...x, year: e.target.value } : x)) })} />
                </Field>
                <label className="flex items-center gap-2 pb-2 text-sm">
                  <input type="checkbox" checked={r.contactable} onChange={(e) => set({ references: d.references.map((x, j) => (j === i ? { ...x, contactable: e.target.checked } : x)) })} />
                  Contactable
                </label>
                <Button type="button" variant="ghost" size="icon" aria-label={`Remove reference ${i + 1}`} onClick={() => set({ references: d.references.filter((_, j) => j !== i) })}>
                  <Trash2 />
                </Button>
                <div className="sm:col-span-5">
                  <Field id={`ref-${i}-scope`} label="Scope">
                    <Textarea id={`ref-${i}-scope`} rows={2} value={r.scope} onChange={(e) => set({ references: d.references.map((x, j) => (j === i ? { ...x, scope: e.target.value } : x)) })} />
                  </Field>
                </div>
              </div>
            ))}
            <Button type="button" variant="outline" size="sm" onClick={() => set({ references: [...d.references, { client: "", scope: "", value: "", year: "", contactable: true }] })}>
              <Plus /> Add reference
            </Button>
          </CardContent>
        </Card>
      </fieldset>

      {canEdit ? (
        <div className="sticky bottom-0 flex flex-wrap items-center gap-3 border-t bg-background/95 py-3 backdrop-blur">
          <Button type="submit" disabled={save.isPending}>
            {save.isPending ? "Saving…" : profile ? "Save profile" : "Create profile"}
          </Button>
          {save.isError ? (
            <p role="alert" className="text-sm text-destructive">
              {save.error.message}
            </p>
          ) : save.isSuccess ? (
            <p role="status" className="text-sm text-muted-foreground">
              Saved.
            </p>
          ) : null}
        </div>
      ) : null}
    </form>
  );
}
