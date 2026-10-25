import Link from "next/link";
import { notFound } from "next/navigation";
import { ArrowLeft, ExternalLink } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { DisclaimerBanner } from "@/components/disclaimer-banner";
import { MatchStatusControl } from "@/components/matches/match-status-control";
import { DocumentsCard } from "@/components/tenders/documents-card";
import { HistoryCard } from "@/components/tenders/history-card";
import { QualificationCard } from "@/components/tenders/qualification-card";
import { RequirementsCard } from "@/components/tenders/requirements-card";
import { TenderActions } from "@/components/tenders/tender-actions";
import { serverApi } from "@/lib/api/server";
import { getSession, hasRole } from "@/lib/auth/session";
import { BuyerType, ClassificationStage, Province, SubmissionMethod, TenderStatus, label, tenderStatusTone } from "@/lib/enums";
import { closingIn, formatDateTime } from "@/lib/format";

export const metadata = { title: "Tender" };

function Fact({ term, children }: { term: string; children: React.ReactNode }) {
  return (
    <div>
      <dt className="text-xs text-muted-foreground">{term}</dt>
      <dd className="text-sm">{children}</dd>
    </div>
  );
}

/** Tender detail (PLAN.md §11): header, match status, summary, Can we bid?, requirements with sources, documents, history. */
export default async function TenderPage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = await params;
  const client = await serverApi();
  const path = { params: { path: { id } } };
  const [detail, requirements, qualification, history, matches, session] = await Promise.all([
    client.GET("/api/v1/tenders/{id}", path),
    client.GET("/api/v1/tenders/{id}/requirements", path),
    client.GET("/api/v1/tenders/{id}/qualification", path),
    client.GET("/api/v1/tenders/{id}/history", path),
    client.GET("/api/v1/matches", { params: { query: { TenderId: id, PageSize: 20 } } }),
    getSession(),
  ]);
  const tender = detail.data;
  if (detail.response.status === 404 || !tender) {
    notFound();
  }

  const canEdit = session ? hasRole(session.user, "Reviewer") : false;
  const thresholds = (requirements.data?.functional ?? []).map((r) => r.threshold).filter((t): t is number => t !== null);
  const functionalityThreshold = thresholds.length > 0 ? Math.max(...thresholds) : null;
  const countdown = closingIn(tender.closingAt);
  const tenderMatches = matches.data?.items ?? [];

  return (
    <div className="space-y-6">
      <Link href="/tenders" className="inline-flex items-center gap-1 text-sm text-muted-foreground hover:text-foreground">
        <ArrowLeft className="size-4" aria-hidden /> Tenders
      </Link>

      <header className="space-y-3">
        <div className="flex flex-wrap items-start justify-between gap-3">
          <div className="min-w-0 space-y-1">
            <div className="font-mono text-sm text-muted-foreground">{tender.bidNumber}</div>
            <h1 className="text-2xl font-semibold tracking-tight">{tender.title}</h1>
          </div>
          <Badge variant={tenderStatusTone(tender.status)}>{label(TenderStatus, tender.status)}</Badge>
        </div>
        <dl className="grid grid-cols-2 gap-x-6 gap-y-3 sm:grid-cols-3 lg:grid-cols-6">
          <Fact term="Buyer">
            {tender.buyerName} <span className="text-muted-foreground">({label(BuyerType, tender.buyerType)})</span>
          </Fact>
          <Fact term="Province">{label(Province, tender.province)}</Fact>
          <Fact term="Closing">
            {formatDateTime(tender.closingAt)}
            {countdown ? <span className="block text-xs text-muted-foreground">{countdown}</span> : null}
          </Fact>
          <Fact term="Briefing">
            {tender.briefingAt ? formatDateTime(tender.briefingAt) : "None stated"}
            {tender.briefingCompulsory ? <span className="block text-xs font-medium text-destructive">Compulsory</span> : null}
          </Fact>
          <Fact term="Submission">{label(SubmissionMethod, tender.submissionMethod)}</Fact>
          <Fact term="Relevance">
            {tender.classification ? `${tender.classification.relevance} (${label(ClassificationStage, tender.classification.stage)})` : "Not classified"}
          </Fact>
        </dl>
        <a href={tender.sourceUrl} target="_blank" rel="noreferrer" className="inline-flex items-center gap-1 text-sm text-primary hover:underline">
          <ExternalLink className="size-4" aria-hidden /> Open on eTenders
          <span className="sr-only">(opens in a new tab)</span>
        </a>
      </header>

      <DisclaimerBanner />

      {canEdit ? <TenderActions tenderId={tender.id} /> : null}

      {tenderMatches.length > 0 ? (
        <Card id="match">
          <CardHeader>
            <CardTitle>Match</CardTitle>
            <CardDescription>Where this tender is in the team&apos;s workflow. Changing it records a decision; nothing is submitted.</CardDescription>
          </CardHeader>
          <CardContent className="space-y-6">
            {tenderMatches.map((m) => (
              <div key={m.id} className="space-y-2">
                {tenderMatches.length > 1 ? <div className="text-sm font-medium">{m.companyName}</div> : null}
                <MatchStatusControl canEdit={canEdit} match={{ id: m.id, status: m.status, notes: m.notes, dismissReason: m.dismissReason }} />
              </div>
            ))}
          </CardContent>
        </Card>
      ) : null}

      <Card id="summary">
        <CardHeader>
          <CardTitle>Summary</CardTitle>
        </CardHeader>
        <CardContent className="space-y-3 text-sm">
          {tender.summary ? <p className="max-w-3xl">{tender.summary}</p> : <p className="text-muted-foreground">No summary yet: requirement extraction has not run.</p>}
          <details>
            <summary className="cursor-pointer text-muted-foreground">Description from eTenders</summary>
            <p className="mt-2 max-w-3xl whitespace-pre-wrap">{tender.description}</p>
          </details>
        </CardContent>
      </Card>

      <QualificationCard report={qualification.response.ok ? (qualification.data ?? null) : null} functionalityThreshold={functionalityThreshold} />
      <RequirementsCard tenderId={tender.id} requirements={requirements.data ?? null} />
      <DocumentsCard tenderId={tender.id} documents={tender.documents} />
      <HistoryCard history={history.data ?? null} />
    </div>
  );
}
