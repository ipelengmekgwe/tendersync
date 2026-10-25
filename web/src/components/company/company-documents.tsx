"use client";

import { useRef, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Download, Lock, Pencil, Trash2, Upload } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Skeleton } from "@/components/ui/skeleton";
import { ExpiryBadge } from "@/components/expiry-badge";
import { api } from "@/lib/api/client";
import { problemMessage, unwrap } from "@/lib/api/problem";
import type { CompanyDocument } from "@/lib/api/types";
import { formatBytes } from "@/lib/documents";
import { CompanyDocumentType, label } from "@/lib/enums";
import { formatDate } from "@/lib/format";
import { dateInputToIso, isoToDateInput } from "@/lib/lists";

const MAX_BYTES = 25 * 1024 * 1024;

export function companyDocumentDownloadHref(profileId: string, documentId: string) {
  return `/api/proxy/company-profiles/${encodeURIComponent(profileId)}/documents/${encodeURIComponent(documentId)}/download`;
}

/**
 * /company/documents. Files go to the API as multipart through the proxy and are encrypted there with a per-file key
 * before they reach the store; downloads are audited. Expiry drives the badges, the overview tile and the reminders.
 */
export function CompanyDocuments({ profileId, canDelete }: { profileId: string; canDelete: boolean }) {
  const queryClient = useQueryClient();
  const key = ["company-documents", profileId];
  const fileRef = useRef<HTMLInputElement>(null);
  const [type, setType] = useState("0");
  const [title, setTitle] = useState("");
  const [expires, setExpires] = useState("");
  const [editing, setEditing] = useState<{ doc: CompanyDocument; title: string; expires: string } | null>(null);
  const [deleting, setDeleting] = useState<CompanyDocument | null>(null);

  const documents = useQuery({
    queryKey: key,
    queryFn: async () => unwrap(await api.GET("/api/v1/company-profiles/{id}/documents", { params: { path: { id: profileId } } })),
  });

  const upload = useMutation({
    mutationFn: async () => {
      const file = fileRef.current?.files?.[0];
      if (!file) {
        throw new Error("Choose a file to upload.");
      }
      if (file.size > MAX_BYTES) {
        throw new Error(`The file is ${formatBytes(file.size)}; the limit is ${formatBytes(MAX_BYTES)}.`);
      }
      const form = new FormData();
      form.append("file", file);
      form.append("type", type);
      form.append("title", title.trim() || file.name);
      const expiresAt = dateInputToIso(expires);
      if (expiresAt) {
        form.append("expiresAt", expiresAt);
      }
      const response = await fetch(`/api/proxy/company-profiles/${encodeURIComponent(profileId)}/documents`, { method: "POST", body: form });
      if (!response.ok) {
        throw new Error(problemMessage(await response.json().catch(() => null), response.status));
      }
      return (await response.json()) as CompanyDocument;
    },
    onSuccess: async () => {
      setTitle("");
      setExpires("");
      if (fileRef.current) {
        fileRef.current.value = "";
      }
      await queryClient.invalidateQueries({ queryKey: key });
    },
  });

  const update = useMutation({
    mutationFn: async (e: { doc: CompanyDocument; title: string; expires: string }) =>
      unwrap(
        await api.PUT("/api/v1/company-profiles/{id}/documents/{docId}", {
          params: { path: { id: profileId, docId: e.doc.id } },
          body: { title: e.title.trim(), expiresAt: dateInputToIso(e.expires) },
        }),
      ),
    onSuccess: async () => {
      setEditing(null);
      await queryClient.invalidateQueries({ queryKey: key });
    },
  });

  const remove = useMutation({
    mutationFn: async (doc: CompanyDocument) =>
      unwrap(await api.DELETE("/api/v1/company-profiles/{id}/documents/{docId}", { params: { path: { id: profileId, docId: doc.id } } })),
    onSuccess: async () => {
      setDeleting(null);
      await queryClient.invalidateQueries({ queryKey: key });
    },
  });

  return (
    <div className="space-y-6">
      <Card>
        <CardHeader>
          <CardTitle className="flex items-center gap-2">
            <Lock className="size-4" aria-hidden /> Upload a document
          </CardTitle>
          <CardDescription>Encrypted at rest with AES-256-GCM before it is stored. Up to {formatBytes(MAX_BYTES)}.</CardDescription>
        </CardHeader>
        <CardContent>
          <form
            className="grid gap-3 md:grid-cols-[minmax(0,1fr)_12rem_minmax(0,1fr)_10rem_auto] md:items-end"
            onSubmit={(e) => {
              e.preventDefault();
              upload.mutate();
            }}
          >
            <div className="space-y-1">
              <Label htmlFor="doc-file">File</Label>
              <Input id="doc-file" ref={fileRef} type="file" required />
            </div>
            <div className="space-y-1">
              <Label htmlFor="doc-type">Type</Label>
              <select id="doc-type" className="h-9 w-full rounded-md border bg-background px-2 text-sm" value={type} onChange={(e) => setType(e.target.value)}>
                {Object.entries(CompanyDocumentType).map(([value, name]) => (
                  <option key={value} value={value}>
                    {name}
                  </option>
                ))}
              </select>
            </div>
            <div className="space-y-1">
              <Label htmlFor="doc-title">Title</Label>
              <Input id="doc-title" value={title} maxLength={300} placeholder="Defaults to the file name" onChange={(e) => setTitle(e.target.value)} />
            </div>
            <div className="space-y-1">
              <Label htmlFor="doc-expires">Expires</Label>
              <Input id="doc-expires" type="date" value={expires} onChange={(e) => setExpires(e.target.value)} />
            </div>
            <Button type="submit" disabled={upload.isPending}>
              <Upload /> {upload.isPending ? "Uploading…" : "Upload"}
            </Button>
          </form>
          {upload.isError ? (
            <p role="alert" className="mt-2 text-sm text-destructive">
              {upload.error.message}
            </p>
          ) : upload.isSuccess ? (
            <p role="status" className="mt-2 text-sm text-muted-foreground">
              Uploaded and encrypted.
            </p>
          ) : null}
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle>Library</CardTitle>
          <CardDescription>Qualification checks these against each tender&apos;s returnable documents.</CardDescription>
        </CardHeader>
        <CardContent>
          {documents.isPending ? (
            <Skeleton className="h-32 w-full" />
          ) : documents.isError ? (
            <p role="alert" className="text-sm text-destructive">
              {documents.error.message}
            </p>
          ) : documents.data.length === 0 ? (
            <p className="text-sm text-muted-foreground">No documents yet.</p>
          ) : (
            <ul className="divide-y rounded-md border" aria-label="Company documents">
              {documents.data.map((doc) => (
                <li key={doc.id} className="flex flex-col gap-2 p-3 text-sm sm:flex-row sm:items-center sm:justify-between" data-testid="company-document">
                  <div className="min-w-0 space-y-1">
                    <div className="truncate font-medium" title={doc.title}>
                      {doc.title}
                    </div>
                    <div className="flex flex-wrap items-center gap-2 text-xs text-muted-foreground">
                      <span>{label(CompanyDocumentType, doc.type)}</span>
                      <span>· {formatBytes(doc.sizeBytes)}</span>
                      <span>· uploaded {formatDate(doc.createdAt)}</span>
                      <ExpiryBadge expiresAt={doc.expiresAt} />
                    </div>
                  </div>
                  <div className="flex shrink-0 flex-wrap items-center gap-2">
                    <a href={companyDocumentDownloadHref(profileId, doc.id)} className="inline-flex items-center gap-1 text-primary hover:underline">
                      <Download className="size-4" aria-hidden /> Download
                      <span className="sr-only"> {doc.title}</span>
                    </a>
                    <Button size="sm" variant="outline" onClick={() => { update.reset(); setEditing({ doc, title: doc.title, expires: isoToDateInput(doc.expiresAt) }); }}>
                      <Pencil /> Edit<span className="sr-only"> {doc.title}</span>
                    </Button>
                    {canDelete ? (
                      <Button size="sm" variant="ghost" aria-label={`Delete ${doc.title}`} onClick={() => { remove.reset(); setDeleting(doc); }}>
                        <Trash2 />
                      </Button>
                    ) : null}
                  </div>
                </li>
              ))}
            </ul>
          )}
        </CardContent>
      </Card>

      <Dialog open={editing !== null} onOpenChange={(open) => (open ? null : setEditing(null))}>
        <DialogContent>
          {editing ? (
            <form
              className="space-y-3"
              onSubmit={(e) => {
                e.preventDefault();
                update.mutate(editing);
              }}
            >
              <DialogHeader>
                <DialogTitle>Edit document</DialogTitle>
                <DialogDescription>The file itself cannot change; upload a new version instead.</DialogDescription>
              </DialogHeader>
              <div className="space-y-1">
                <Label htmlFor="edit-title">Title</Label>
                <Input id="edit-title" value={editing.title} required maxLength={300} onChange={(e) => setEditing({ ...editing, title: e.target.value })} />
              </div>
              <div className="space-y-1">
                <Label htmlFor="edit-expires">Expires</Label>
                <Input id="edit-expires" type="date" value={editing.expires} onChange={(e) => setEditing({ ...editing, expires: e.target.value })} />
              </div>
              {update.isError ? (
                <p role="alert" className="text-sm text-destructive">
                  {update.error.message}
                </p>
              ) : null}
              <DialogFooter>
                <Button type="button" variant="outline" onClick={() => setEditing(null)}>
                  Cancel
                </Button>
                <Button type="submit" disabled={update.isPending}>
                  Save
                </Button>
              </DialogFooter>
            </form>
          ) : null}
        </DialogContent>
      </Dialog>

      <Dialog open={deleting !== null} onOpenChange={(open) => (open ? null : setDeleting(null))}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Delete {deleting?.title}?</DialogTitle>
            <DialogDescription>The document is marked deleted and hidden from the library; qualification stops counting it. The encrypted file stays in the store.</DialogDescription>
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
