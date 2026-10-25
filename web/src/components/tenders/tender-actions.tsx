"use client";

import { useRouter } from "next/navigation";
import { useState } from "react";
import { useMutation } from "@tanstack/react-query";
import { Button } from "@/components/ui/button";

type ActionKey = "reclassify" | "extract" | "qualify" | "download";

const ACTIONS: { key: ActionKey; label: string; path: (id: string) => string; done: string }[] = [
  { key: "download", label: "Download documents", path: (id) => `tenders/${id}/documents/download`, done: "Document download queued." },
  { key: "extract", label: "Extract requirements", path: (id) => `tenders/${id}/extract`, done: "Requirement extraction queued." },
  { key: "qualify", label: "Qualify", path: (id) => `tenders/${id}/qualify`, done: "Qualification queued." },
  { key: "reclassify", label: "Reclassify", path: (id) => `tenders/${id}/reclassify`, done: "Classification updated." },
];

/**
 * Reviewer actions on a tender (PLAN.md §10 "Tender actions"). Extraction and qualification are queued in the Worker,
 * so the page shows the result after the pipeline has run; reclassify answers straight away.
 */
export function TenderActions({ tenderId }: { tenderId: string }) {
  const router = useRouter();
  const [message, setMessage] = useState<{ ok: boolean; text: string } | null>(null);
  const mutation = useMutation({
    mutationFn: async (key: ActionKey) => {
      const action = ACTIONS.find((a) => a.key === key)!;
      const response = await fetch(`/api/proxy/${action.path(tenderId)}`, { method: "POST" });
      if (!response.ok) {
        const problem = (await response.json().catch(() => null)) as { title?: string; detail?: string } | null;
        throw new Error(problem?.detail ?? problem?.title ?? `The API responded ${response.status}.`);
      }
      return action.done;
    },
    onSuccess: (text) => {
      setMessage({ ok: true, text });
      router.refresh();
    },
    onError: (error) => setMessage({ ok: false, text: error.message }),
  });

  return (
    <div className="space-y-2">
      <div className="flex flex-wrap gap-2" role="group" aria-label="Tender actions">
        {ACTIONS.map((a) => (
          <Button key={a.key} size="sm" variant="outline" disabled={mutation.isPending} onClick={() => mutation.mutate(a.key)}>
            {mutation.isPending && mutation.variables === a.key ? "Working…" : a.label}
          </Button>
        ))}
      </div>
      {message ? (
        <p role={message.ok ? "status" : "alert"} className={message.ok ? "text-sm text-muted-foreground" : "text-sm text-destructive"}>
          {message.text}
        </p>
      ) : null}
    </div>
  );
}
