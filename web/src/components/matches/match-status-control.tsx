"use client";

import { useRouter } from "next/navigation";
import { useEffect, useId, useState } from "react";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import { api } from "@/lib/api/client";
import type { Match } from "@/lib/api/types";
import { MatchStatus, label, matchStatusTone } from "@/lib/enums";
import { DISMISS_REASONS, type MatchAction, matchActions } from "@/lib/matches";

export interface MatchState {
  id: string;
  status: number;
  notes: string | null;
  dismissReason: string | null;
}

interface Patch {
  status: number | null;
  notes: string | null;
  dismissReason: string | null;
}

async function patchMatch(id: string, body: Patch): Promise<Match> {
  const { data, error, response } = await api.PATCH("/api/v1/matches/{id}", { params: { path: { id } }, body });
  if (error || !data) {
    const detail = (error as { title?: string; errors?: Record<string, string[]> } | undefined)?.errors;
    const message = detail ? Object.values(detail).flat().join(" ") : (error as { title?: string } | undefined)?.title;
    throw new Error(message || `The API responded ${response.status}.`);
  }
  return data;
}

/**
 * Status badge, quick actions and (in full mode) the notes editor for one match. Every change is a PATCH through the
 * proxy; afterwards the inbox queries are invalidated and server components re-render. Actions only record decisions
 * (ADR-015). Viewers see the status without the controls.
 */
export function MatchStatusControl({ match, canEdit, compact = false }: { match: MatchState; canEdit: boolean; compact?: boolean }) {
  const router = useRouter();
  const queryClient = useQueryClient();
  const [state, setState] = useState(match);
  const [notes, setNotes] = useState(match.notes ?? "");
  const [dismissOpen, setDismissOpen] = useState(false);
  const [reason, setReason] = useState("");
  const notesId = useId();
  const reasonId = useId();

  // Server data wins when it changes (another tab, a refetch); typing in the notes box is left alone otherwise.
  useEffect(() => {
    setState({ id: match.id, status: match.status, notes: match.notes, dismissReason: match.dismissReason });
    setNotes(match.notes ?? "");
  }, [match.id, match.status, match.notes, match.dismissReason]);

  const mutation = useMutation({
    mutationFn: (body: Patch) => patchMatch(state.id, body),
    onSuccess: async (updated) => {
      setState({ id: updated.id, status: updated.status, notes: updated.notes, dismissReason: updated.dismissReason });
      setNotes(updated.notes ?? "");
      setDismissOpen(false);
      setReason("");
      await queryClient.invalidateQueries({ queryKey: ["matches"] });
      router.refresh();
    },
  });

  function run(action: MatchAction) {
    if (action.needsReason) {
      mutation.reset();
      setDismissOpen(true);
      return;
    }
    mutation.mutate({ status: action.to, notes: null, dismissReason: null });
  }

  const statusLabel = label(MatchStatus, state.status);

  return (
    <div className={compact ? "flex flex-wrap items-center gap-2" : "space-y-3"}>
      <div className="flex flex-wrap items-center gap-2">
        <Badge variant={matchStatusTone(state.status)} data-testid="match-status">
          {statusLabel}
        </Badge>
        {state.status === 2 && state.dismissReason ? <span className="text-xs text-muted-foreground">{state.dismissReason}</span> : null}
      </div>

      {canEdit ? (
        <div className="flex flex-wrap gap-2" role="group" aria-label="Change match status">
          {matchActions(state.status).map((action, index) => (
            <Button
              key={action.to}
              size="sm"
              variant={action.needsReason ? "outline" : index === 0 ? "default" : "secondary"}
              disabled={mutation.isPending}
              onClick={() => run(action)}
            >
              {action.label}
            </Button>
          ))}
        </div>
      ) : null}

      {!compact && canEdit ? (
        <form
          className="space-y-2"
          onSubmit={(e) => {
            e.preventDefault();
            mutation.mutate({ status: null, notes, dismissReason: null });
          }}
        >
          <Label htmlFor={notesId}>Notes</Label>
          <Textarea id={notesId} value={notes} maxLength={4000} rows={3} onChange={(e) => setNotes(e.target.value)} placeholder="What the team decided and why" />
          <Button type="submit" size="sm" variant="secondary" disabled={mutation.isPending || notes === (state.notes ?? "")}>
            Save notes
          </Button>
        </form>
      ) : !compact && state.notes ? (
        <p className="text-sm whitespace-pre-wrap">{state.notes}</p>
      ) : null}

      {mutation.isError && !dismissOpen ? (
        <p role="alert" className="text-sm text-destructive">
          {mutation.error.message}
        </p>
      ) : null}

      <Dialog open={dismissOpen} onOpenChange={setDismissOpen}>
        <DialogContent>
          <form
            className="space-y-4"
            onSubmit={(e) => {
              e.preventDefault();
              mutation.mutate({ status: 2, notes: null, dismissReason: reason.trim() });
            }}
          >
            <DialogHeader>
              <DialogTitle>Dismiss this match</DialogTitle>
              <DialogDescription>The reason is kept with the match so the team can see why it was passed over.</DialogDescription>
            </DialogHeader>
            <div className="flex flex-wrap gap-2">
              {DISMISS_REASONS.map((r) => (
                <Button key={r} type="button" size="xs" variant={reason === r ? "default" : "outline"} onClick={() => setReason(r)}>
                  {r}
                </Button>
              ))}
            </div>
            <div className="space-y-1">
              <Label htmlFor={reasonId}>Reason</Label>
              <Textarea id={reasonId} value={reason} maxLength={1024} rows={2} onChange={(e) => setReason(e.target.value)} required />
            </div>
            {mutation.isError ? (
              <p role="alert" className="text-sm text-destructive">
                {mutation.error.message}
              </p>
            ) : null}
            <DialogFooter>
              <Button type="button" variant="outline" onClick={() => setDismissOpen(false)}>
                Cancel
              </Button>
              <Button type="submit" disabled={mutation.isPending || reason.trim() === ""}>
                Dismiss
              </Button>
            </DialogFooter>
          </form>
        </DialogContent>
      </Dialog>
    </div>
  );
}
