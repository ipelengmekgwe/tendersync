"use client";

import { useId, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Pencil, Plus, Trash2 } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Skeleton } from "@/components/ui/skeleton";
import { Textarea } from "@/components/ui/textarea";
import { CategoryTester } from "@/components/settings/category-tester";
import { api } from "@/lib/api/client";
import { unwrap } from "@/lib/api/problem";
import type { InterestCategory } from "@/lib/api/types";
import { formatList, parseList } from "@/lib/lists";

interface Draft {
  id: string | null;
  code: string;
  name: string;
  description: string;
  keywords: string;
  negativeKeywords: string;
  weight: number;
  enabled: boolean;
}

const EMPTY: Draft = { id: null, code: "", name: "", description: "", keywords: "", negativeKeywords: "", weight: 1, enabled: true };

function toDraft(c: InterestCategory): Draft {
  return {
    id: c.id,
    code: c.code,
    name: c.name,
    description: c.description ?? "",
    keywords: formatList(c.keywords, "\n"),
    negativeKeywords: formatList(c.negativeKeywords, "\n"),
    weight: c.weight,
    enabled: c.enabled,
  };
}

function toInput(d: Draft) {
  return {
    name: d.name.trim(),
    description: d.description.trim() || null,
    keywords: parseList(d.keywords),
    negativeKeywords: parseList(d.negativeKeywords),
    weight: d.weight,
    enabled: d.enabled,
  };
}

export const CATEGORIES_KEY = ["interest-categories"] as const;

/**
 * Interest categories (PLAN.md §11 /settings/categories). Keyword changes apply from the next classification; the tester
 * below dry-runs the keyword stage against a stored tender and can reclassify it straight away.
 */
export function CategoriesManager() {
  const queryClient = useQueryClient();
  const [draft, setDraft] = useState<Draft | null>(null);
  const [deleting, setDeleting] = useState<InterestCategory | null>(null);
  const ids = { code: useId(), name: useId(), description: useId(), keywords: useId(), negatives: useId(), weight: useId(), enabled: useId() };

  const categories = useQuery({
    queryKey: CATEGORIES_KEY,
    queryFn: async () => unwrap(await api.GET("/api/v1/interest-categories")),
  });

  const save = useMutation({
    mutationFn: async (d: Draft) =>
      d.id
        ? unwrap(await api.PUT("/api/v1/interest-categories/{id}", { params: { path: { id: d.id } }, body: toInput(d) }))
        : unwrap(await api.POST("/api/v1/interest-categories", { body: { code: d.code.trim(), ...toInput(d) } })),
    onSuccess: async () => {
      setDraft(null);
      await queryClient.invalidateQueries({ queryKey: CATEGORIES_KEY });
    },
  });

  const toggle = useMutation({
    mutationFn: async (c: InterestCategory) =>
      unwrap(await api.PUT("/api/v1/interest-categories/{id}", { params: { path: { id: c.id } }, body: { ...toInput(toDraft(c)), enabled: !c.enabled } })),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: CATEGORIES_KEY }),
  });

  const remove = useMutation({
    mutationFn: async (c: InterestCategory) => unwrap(await api.DELETE("/api/v1/interest-categories/{id}", { params: { path: { id: c.id } } })),
    onSuccess: async () => {
      setDeleting(null);
      await queryClient.invalidateQueries({ queryKey: CATEGORIES_KEY });
    },
  });

  return (
    <div className="space-y-6">
      <Card>
        <CardHeader>
          <CardTitle className="flex flex-wrap items-center justify-between gap-2">
            Interest categories
            <Button size="sm" onClick={() => { save.reset(); setDraft(EMPTY); }}>
              <Plus /> New category
            </Button>
          </CardTitle>
          <CardDescription>
            A tender is a candidate when a keyword appears as a whole word in its title (×3), description (×1) or document titles (×2), times the category
            weight, less 2 per negative keyword. Changes apply from the next classification; reclassify a tender to see them now.
          </CardDescription>
        </CardHeader>
        <CardContent>
          {categories.isPending ? (
            <div className="space-y-2" aria-busy="true">
              {Array.from({ length: 5 }).map((_, i) => (
                <Skeleton key={i} className="h-12 w-full" />
              ))}
            </div>
          ) : categories.isError ? (
            <p role="alert" className="text-sm text-destructive">
              {categories.error.message}
            </p>
          ) : (
            <ul className="divide-y rounded-md border" aria-label="Interest categories">
              {categories.data.map((c) => (
                <li key={c.id} className="grid gap-2 p-3 text-sm md:grid-cols-[minmax(0,1fr)_auto] md:items-center" data-testid="category-row">
                  <div className="min-w-0 space-y-1">
                    <div className="flex flex-wrap items-center gap-2">
                      <span className="font-mono text-xs">{c.code}</span>
                      <span className="font-medium">{c.name}</span>
                      {!c.enabled ? <Badge variant="outline">Disabled</Badge> : null}
                      <span className="text-xs text-muted-foreground">weight {c.weight}</span>
                    </div>
                    <p className="line-clamp-2 text-xs text-muted-foreground">
                      {c.keywords.length} keyword{c.keywords.length === 1 ? "" : "s"}: {formatList(c.keywords.slice(0, 12))}
                      {c.keywords.length > 12 ? "…" : ""}
                      {c.negativeKeywords.length > 0 ? ` · not: ${formatList(c.negativeKeywords.slice(0, 6))}` : ""}
                    </p>
                  </div>
                  <div className="flex flex-wrap items-center gap-2">
                    <label className="flex items-center gap-1.5 text-xs">
                      <input type="checkbox" checked={c.enabled} disabled={toggle.isPending} onChange={() => toggle.mutate(c)} />
                      Enabled
                    </label>
                    <Button size="sm" variant="outline" onClick={() => { save.reset(); setDraft(toDraft(c)); }} aria-label={`Edit ${c.code}`}>
                      <Pencil /> Edit
                    </Button>
                    <Button size="sm" variant="ghost" onClick={() => { remove.reset(); setDeleting(c); }} aria-label={`Delete ${c.code}`}>
                      <Trash2 />
                    </Button>
                  </div>
                </li>
              ))}
            </ul>
          )}
          {toggle.isError ? (
            <p role="alert" className="mt-2 text-sm text-destructive">
              {toggle.error.message}
            </p>
          ) : null}
        </CardContent>
      </Card>

      <CategoryTester />

      <Dialog open={draft !== null} onOpenChange={(open) => (open ? null : setDraft(null))}>
        <DialogContent className="sm:max-w-xl">
          {draft ? (
            <form
              className="space-y-3"
              onSubmit={(e) => {
                e.preventDefault();
                save.mutate(draft);
              }}
            >
              <DialogHeader>
                <DialogTitle>{draft.id ? `Edit ${draft.code}` : "New category"}</DialogTitle>
                <DialogDescription>Keywords are matched as whole words, case-insensitive. One per line or comma-separated.</DialogDescription>
              </DialogHeader>
              {!draft.id ? (
                <div className="space-y-1">
                  <Label htmlFor={ids.code}>Code</Label>
                  <Input id={ids.code} value={draft.code} maxLength={40} required placeholder="e.g. GIS" onChange={(e) => setDraft({ ...draft, code: e.target.value.toUpperCase() })} />
                </div>
              ) : null}
              <div className="space-y-1">
                <Label htmlFor={ids.name}>Name</Label>
                <Input id={ids.name} value={draft.name} maxLength={200} required onChange={(e) => setDraft({ ...draft, name: e.target.value })} />
              </div>
              <div className="space-y-1">
                <Label htmlFor={ids.description}>Description</Label>
                <Input id={ids.description} value={draft.description} maxLength={2000} onChange={(e) => setDraft({ ...draft, description: e.target.value })} />
              </div>
              <div className="space-y-1">
                <Label htmlFor={ids.keywords}>Keywords</Label>
                <Textarea className="max-h-48 overflow-y-auto" id={ids.keywords} rows={6} value={draft.keywords} onChange={(e) => setDraft({ ...draft, keywords: e.target.value })} />
              </div>
              <div className="space-y-1">
                <Label htmlFor={ids.negatives}>Negative keywords</Label>
                <Textarea className="max-h-48 overflow-y-auto" id={ids.negatives} rows={3} value={draft.negativeKeywords} onChange={(e) => setDraft({ ...draft, negativeKeywords: e.target.value })} />
              </div>
              <div className="flex flex-wrap items-end gap-4">
                <div className="space-y-1">
                  <Label htmlFor={ids.weight}>Weight (1–10)</Label>
                  <Input
                    id={ids.weight}
                    type="number"
                    min={1}
                    max={10}
                    className="w-24"
                    value={draft.weight}
                    onChange={(e) => setDraft({ ...draft, weight: Number(e.target.value) })}
                  />
                </div>
                <label className="flex items-center gap-2 pb-2 text-sm" htmlFor={ids.enabled}>
                  <input id={ids.enabled} type="checkbox" checked={draft.enabled} onChange={(e) => setDraft({ ...draft, enabled: e.target.checked })} />
                  Enabled
                </label>
              </div>
              {save.isError ? (
                <p role="alert" className="text-sm text-destructive">
                  {save.error.message}
                </p>
              ) : null}
              <DialogFooter>
                <Button type="button" variant="outline" onClick={() => setDraft(null)}>
                  Cancel
                </Button>
                <Button type="submit" disabled={save.isPending}>
                  {save.isPending ? "Saving…" : "Save"}
                </Button>
              </DialogFooter>
            </form>
          ) : null}
        </DialogContent>
      </Dialog>

      <Dialog open={deleting !== null} onOpenChange={(open) => (open ? null : setDeleting(null))}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Delete {deleting?.code}?</DialogTitle>
            <DialogDescription>Past classifications keep the code. Disabling the category instead can be undone.</DialogDescription>
          </DialogHeader>
          {remove.isError ? (
            <p role="alert" className="text-sm text-destructive">
              {remove.error.message}
            </p>
          ) : null}
          <DialogFooter>
            <Button variant="outline" onClick={() => setDeleting(null)}>
              Cancel
            </Button>
            <Button variant="destructive" disabled={remove.isPending} onClick={() => deleting && remove.mutate(deleting)}>
              Delete
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
  );
}
