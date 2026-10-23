# Tender Portal — Project Plan

Working name: **TenderPilot** (placeholder, rename later)
Owner: Ipeleng
Kick-off: weekend of 24–25 October 2026
Goal: a South African tender aggregator that beats TenderFlow on *usefulness per tender*, not on volume.

---

## 1. The idea in one paragraph

Every SA tender portal pulls the same free data from National Treasury's eTenders OCDS API, so the data itself is not the product. We win by doing the work a bidder has to do by hand today: reading the bid pack, pulling out the qualifying criteria, and telling a business whether it can realistically bid. Start narrow (eTenders only, one niche), prove the eligibility checklist is worth paying for, then widen coverage.

## 2. What "better than TenderFlow" means

| TenderFlow today | What we do instead |
|---|---|
| Notice-level listing (title, buyer, closing date) | Parsed bid-pack summary: CIDB grade, B-BBEE level, compulsory briefing, functionality criteria, required documents |
| Keyword alerts | Company-profile matching: only show tenders the business can qualify for |
| Email alerts | Email first, WhatsApp later |
| Award list | Award intelligence: who wins what, from which buyer, at what value |
| Broad categories | Clean, specific taxonomy; start with IT and professional services |

Non-goals for v1: bid document authoring, submissions, payments, municipal/SOE scrapers.

## 3. Data sources

**Primary (v1):** National Treasury eTenders OCDS API
- Swagger: `https://ocds-api.etenders.gov.za/swagger/index.html`
- Releases endpoint takes a date range (required) and paginates; follow the `links.next` URL
- OCID prefix: `ocds-9t57fa-<tenderId>`
- Each process can have several releases over time (planning → tender → award) — merge by ocid
- Bid-pack PDFs are linked under `tender.documents[]` and download from `etenders.gov.za/home/Download?...`
- Known quirks: occasional duplicate release IDs; municipalities and SOEs only appear if they publish voluntarily

**Later (v2+):** scrapers for City of Cape Town, Joburg, eThekwini, Transnet, Eskom, SANRAL; Government Tender Bulletin

**Sanity check:** TenderFlow's own public JSON at `tenderflow.co.za/data` — useful to compare counts and categories, not to copy data from.

## 4. Architecture (v1)

```
eTenders OCDS API ──► Ingest job (every 2h) ──► Postgres
                                                  │
         bid-pack PDFs ──► Parse job (LLM) ───────┤
                                                  ▼
                                    Web app (search, profile, alerts)
                                                  │
                                           Email alert job (daily)
```

**Stack decision (to confirm Saturday morning):**
- Option A — Node/TypeScript + Next.js on Vercel, Postgres on Neon/Supabase. Matches Router_UI and the ipeleng.dev setup; fastest path to a deployed URL.
- Option B — .NET 8 API + SQL Server. Matches day-job skills; better fit if the parsing jobs get heavy. Hosting is more work.
- Recommendation: Option A for the weekend prototype. The ingest logic is simple enough to port later if needed.

**Other pieces**
- PDF text extraction: `pdf-parse` (Node) or `pdfplumber` (Python) as a sidecar script
- LLM parsing: Claude API with a strict JSON schema for the extracted fields
- Email: Resend or Postmark
- Auth: Clerk or Supabase Auth
- Scheduling: Vercel Cron or GitHub Actions cron hitting an API route

## 5. Data model (first cut)

- **tender** — ocid, tender_id, title, description, buyer_id, buyer_name, province, category, procurement_method, tender_type, value_amount, published_at, closing_at, briefing_at, briefing_compulsory, status, source_url, raw_json
- **tender_document** — tender_id, title, url, format, downloaded_at, storage_path
- **tender_extraction** — tender_id, cidb_grade, cidb_class, bbbee_min_level, local_content, functionality_threshold, required_documents (json), eligibility_notes, model, extracted_at, confidence
- **buyer** — id, name, type (national / provincial / municipal / SOE), province
- **award** — ocid, supplier_name, value, awarded_at
- **company** — user_id, name, cidb_grade, cidb_classes, bbbee_level, csd_number, provinces, categories
- **alert** — company_id, tender_id, sent_at, match_reason

## 6. Weekend plan

### Saturday — get real data flowing
- [ ] Call the OCDS releases endpoint by hand; look at 5–10 raw releases, note field quirks
- [ ] Decide stack (see §4)
- [ ] Repo + Postgres up
- [ ] Ingest script: pull last 30 days, upsert by ocid, store raw JSON
- [ ] Minimal listing page: title, buyer, province, closing date, link to source
- [ ] Target by end of day: 1 000+ real tenders in the DB and visible in a browser

### Sunday — the differentiator
- [ ] Download bid-pack PDFs for ~20 IT/professional-services tenders
- [ ] Write the extraction prompt + JSON schema; run it on the 20 and check results by hand
- [ ] Tender detail page showing the parsed summary and an eligibility checklist
- [ ] Simple company profile form (CIDB, B-BBEE, provinces, categories)
- [ ] "Can I bid?" badge on each tender based on the profile
- [ ] Target by end of day: a shareable URL that shows a tender with a parsed checklist

### Sunday evening — decide next step
- Review extraction accuracy on the 20 samples. If it's above ~80 % on the key fields, keep going. If not, fix the prompt before building anything else.

## 7. After the weekend (rough roadmap)

**Weeks 1–2:** scheduled ingest, daily email alerts, category cleanup, Western Cape IT niche landing page
**Weeks 3–4:** show it to 10–20 real businesses (contractors, IT consultancies, small suppliers); collect feedback
**Month 2:** award intelligence pages; decide on pricing; first paying users
**Month 3+:** municipal + SOE scrapers, WhatsApp alerts

## 8. Risks

- **Extraction errors cost users real bids.** Every parsed field shows its source page and a "verify in the bid document" note. Never hide the original PDF.
- **Distribution.** Incumbents rank well on Google. Niche landing pages and direct outreach to businesses matter more than features.
- **Time.** Full-time job + Richfield semester. Keep scope to one niche and one differentiator until something sticks.
- **Scrapers rot.** Only add municipal/SOE sources once eTenders-only has paying users.
- **Terms of use.** Confirm eTenders' terms on redistribution before launch; data is public but check the fine print. POPIA applies to contact-person fields.

## 9. Open questions

- Name and domain (subdomain of ipeleng.dev to start?)
- Free tier vs paid from day one?
- Which niche first: IT & professional services, or construction (bigger market, CIDB makes eligibility matching very concrete)?
- Python sidecar for PDF work, or keep everything in one language?

## 10. Definition of done for the weekend

A deployed URL where you can open a real tender from the last 30 days, read a parsed summary of its bid pack, and see whether a given company profile qualifies to bid.
