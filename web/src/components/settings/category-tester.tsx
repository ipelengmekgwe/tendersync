"use client";

import Link from "next/link";
import { useId, useState } from "react";
import { useMutation } from "@tanstack/react-query";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { api } from "@/lib/api/client";
import { unwrap } from "@/lib/api/problem";
import type { ClassificationOutcome, PrefilterResult } from "@/lib/api/types";

interface Tested {
  tender: { id: string; bidNumber: string; title: string };
  result: PrefilterResult;
}

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

/** Finds a stored tender by id, or by bid number/title through the list search, and returns the best hit. */
async function findTender(term: string) {
  if (GUID.test(term)) {
    const tender = unwrap(await api.GET("/api/v1/tenders/{id}", { params: { path: { id: term } } }));
    return { id: tender.id, bidNumber: tender.bidNumber, title: tender.title };
  }
  const page = unwrap(await api.GET("/api/v1/tenders", { params: { query: { Q: term, PageSize: 10, Sort: 1 } } }));
  const exact = page.items.find((t) => t.bidNumber.toLowerCase() === term.toLowerCase());
  const hit = exact ?? page.items[0];
  if (!hit) {
    throw new Error(`No stored tender matches "${term}".`);
  }
  return { id: hit.id, bidNumber: hit.bidNumber, title: hit.title };
}

/**
 * "Test against a tender" (PLAN.md §11): runs the keyword stage with the current categories for a stored tender, shows
 * which keywords hit where, and offers to reclassify the tender so a match can appear or change at once.
 */
export function CategoryTester() {
  const [term, setTerm] = useState("");
  const termId = useId();

  const test = useMutation({
    mutationFn: async (value: string): Promise<Tested> => {
      const tender = await findTender(value.trim());
      const result = unwrap(await api.POST("/api/v1/interest-categories/test", { body: { tenderId: tender.id } }));
      return { tender, result };
    },
  });

  const reclassify = useMutation({
    mutationFn: async (tenderId: string): Promise<ClassificationOutcome> =>
      unwrap(await api.POST("/api/v1/tenders/{id}/reclassify", { params: { path: { id: tenderId } } })),
  });

  return (
    <Card>
      <CardHeader>
        <CardTitle>Test against a tender</CardTitle>
        <CardDescription>Dry-runs the keyword stage with the categories above. Nothing is saved until you reclassify.</CardDescription>
      </CardHeader>
      <CardContent className="space-y-4 text-sm">
        <form
          className="flex flex-wrap items-end gap-2"
          onSubmit={(e) => {
            e.preventDefault();
            reclassify.reset();
            if (term.trim()) {
              test.mutate(term);
            }
          }}
        >
          <div className="min-w-64 flex-1 space-y-1">
            <Label htmlFor={termId}>Bid number, title words or tender id</Label>
            <Input id={termId} value={term} onChange={(e) => setTerm(e.target.value)} placeholder="e.g. DEMO/E2E/0002" />
          </div>
          <Button type="submit" disabled={test.isPending}>
            {test.isPending ? "Testing…" : "Test"}
          </Button>
        </form>

        {test.isError ? (
          <p role="alert" className="text-destructive">
            {test.error.message}
          </p>
        ) : null}

        {test.data ? (
          <section className="space-y-3" aria-label="Keyword stage result" data-testid="prefilter-result">
            <div className="space-y-1">
              <div className="flex flex-wrap items-center gap-2">
                <Link href={`/tenders/${test.data.tender.id}`} className="font-mono text-xs hover:underline">
                  {test.data.tender.bidNumber}
                </Link>
                <span className="font-medium">{test.data.tender.title}</span>
              </div>
              <div className="flex flex-wrap items-center gap-2">
                <Badge variant={test.data.result.isCandidate ? "default" : "outline"} data-testid="prefilter-verdict">
                  {test.data.result.isCandidate ? "Candidate: goes to the model" : "Not a candidate"}
                </Badge>
                <span className="text-muted-foreground">{test.data.result.reason}</span>
              </div>
            </div>
            {test.data.result.scores.length > 0 ? (
              <div className="overflow-x-auto">
                <table className="w-full text-left">
                  <thead className="text-xs text-muted-foreground">
                    <tr>
                      <th className="py-1 pr-3 font-medium">Category</th>
                      <th className="py-1 pr-3 font-medium tabular-nums">Score</th>
                      <th className="py-1 font-medium">Hits</th>
                    </tr>
                  </thead>
                  <tbody>
                    {test.data.result.scores.map((s) => (
                      <tr key={s.code} className="border-t align-top">
                        <td className="py-1.5 pr-3 font-mono text-xs">{s.code}</td>
                        <td className="py-1.5 pr-3 tabular-nums">{s.score}</td>
                        <td className="py-1.5 text-xs">
                          {s.hits.map((h) => `${h.keyword} (${h.field} +${h.weight})`).join(", ")}
                          {s.negativeHits.length > 0 ? (
                            <span className="text-destructive"> · {s.negativeHits.map((h) => `${h.keyword} (${h.weight})`).join(", ")}</span>
                          ) : null}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            ) : null}
            <div className="flex flex-wrap items-center gap-2">
              <Button variant="secondary" size="sm" disabled={reclassify.isPending} onClick={() => reclassify.mutate(test.data.tender.id)}>
                {reclassify.isPending ? "Reclassifying…" : "Reclassify this tender"}
              </Button>
              <span className="text-xs text-muted-foreground">Runs the keyword stage and, for candidates, the model (a few cents).</span>
            </div>
            {reclassify.isError ? (
              <p role="alert" className="text-destructive">
                {reclassify.error.message}
              </p>
            ) : null}
            {reclassify.data ? (
              <p role="status" data-testid="reclassify-outcome">
                Relevance {reclassify.data.relevance}
                {reclassify.data.categoryCodes.length > 0 ? ` (${reclassify.data.categoryCodes.join(", ")})` : ""}: {reclassify.data.matchesCreated} match
                {reclassify.data.matchesCreated === 1 ? "" : "es"} created, {reclassify.data.matchesUpdated} updated.{" "}
                <Link href={`/tenders/${reclassify.data.tenderId}`} className="text-primary hover:underline">
                  Open the tender
                </Link>
              </p>
            ) : null}
          </section>
        ) : null}
      </CardContent>
    </Card>
  );
}
