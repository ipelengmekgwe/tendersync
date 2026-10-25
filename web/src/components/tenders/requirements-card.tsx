import Link from "next/link";
import { AlertTriangle, FileText } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import type { Requirement, TenderRequirements } from "@/lib/api/types";
import { RequirementKind, RequirementType, label } from "@/lib/enums";
import { documentViewerHref } from "@/lib/documents";
import { formatDateTime } from "@/lib/format";

/** The source badge: document and page, opening the viewer at that page with the quote shown above it. */
function SourceBadge({ tenderId, requirement }: { tenderId: string; requirement: Requirement }) {
  if (!requirement.sourceDocumentId) {
    return <span className="text-xs text-muted-foreground">No source</span>;
  }
  const where = requirement.sourcePage ? `p. ${requirement.sourcePage}` : "page unknown";
  return (
    <Link
      href={documentViewerHref(tenderId, requirement.sourceDocumentId, requirement.sourcePage, requirement.sourceQuote)}
      className="inline-flex max-w-full items-center gap-1 rounded-md border px-1.5 py-0.5 text-xs hover:bg-muted"
      title={requirement.sourceQuote ?? undefined}
      data-testid="requirement-source"
    >
      <FileText className="size-3 shrink-0" aria-hidden />
      <span className="truncate">{requirement.sourceDocumentTitle ?? "Document"}</span>{" "}
      <span className="shrink-0 font-medium">{where}</span>
      <span className="sr-only">(open the document{requirement.sourcePage ? ` at page ${requirement.sourcePage}` : ""})</span>
    </Link>
  );
}

function Section({ title, tenderId, items }: { title: string; tenderId: string; items: Requirement[] }) {
  if (items.length === 0) {
    return null;
  }
  return (
    <section className="space-y-2">
      <h2 className="font-medium">
        {title} <span className="text-xs font-normal text-muted-foreground">({items.length})</span>
      </h2>
      <ul className="divide-y rounded-md border">
        {items.map((r) => (
          <li key={r.id} className="grid gap-2 p-3 text-sm md:grid-cols-[minmax(0,1fr)_14rem] md:items-start">
            <div className="min-w-0 space-y-1">
              <div>{r.text}</div>
              <div className="flex flex-wrap items-center gap-1.5 text-xs text-muted-foreground">
                <span>{label(RequirementKind, r.kind)}</span>
                {r.type !== 0 ? <span>· {label(RequirementType, r.type)}</span> : null}
                {r.mandatory && r.kind !== 0 ? <Badge variant="secondary">Mandatory</Badge> : null}
                {r.weight !== null ? <span>· weight {r.weight}</span> : null}
                {r.threshold !== null ? <span>· threshold {r.threshold}</span> : null}
                {r.needsVerification ? (
                  <Badge variant="outline" className="gap-1 border-amber-500/60 text-amber-700 dark:text-amber-400">
                    <AlertTriangle className="size-3" aria-hidden />
                    Verify
                  </Badge>
                ) : null}
              </div>
              {r.sourceQuote ? <blockquote className="border-l-2 pl-2 text-xs text-muted-foreground italic">{r.sourceQuote}</blockquote> : null}
            </div>
            <div className="md:text-right">
              <SourceBadge tenderId={tenderId} requirement={r} />
            </div>
          </li>
        ))}
      </ul>
    </section>
  );
}

/** Requirements with source badges (PLAN.md §11); "Verify" marks low confidence or a missing page. */
export function RequirementsCard({ tenderId, requirements }: { tenderId: string; requirements: TenderRequirements | null }) {
  if (!requirements || requirements.total === 0) {
    return (
      <Card id="requirements">
        <CardHeader>
          <CardTitle>Requirements</CardTitle>
          <CardDescription>Not extracted yet. Extraction reads the bid pack once the documents are downloaded.</CardDescription>
        </CardHeader>
      </Card>
    );
  }
  return (
    <Card id="requirements">
      <CardHeader>
        <CardTitle>Requirements</CardTitle>
        <CardDescription>
          {requirements.total} extracted {formatDateTime(requirements.extractedAt)} · {requirements.needsVerification} to verify. Click a source to open the
          document at that page.
        </CardDescription>
      </CardHeader>
      <CardContent className="space-y-5">
        <Section title="Mandatory" tenderId={tenderId} items={requirements.mandatory} />
        <Section title="Functionality" tenderId={tenderId} items={requirements.functional} />
        <Section title="Returnable documents" tenderId={tenderId} items={requirements.documents} />
        <Section title="Briefing, submission and other" tenderId={tenderId} items={requirements.other} />
      </CardContent>
    </Card>
  );
}
