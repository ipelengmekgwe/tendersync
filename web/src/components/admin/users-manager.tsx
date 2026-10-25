"use client";

import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Pencil, UserPlus } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Skeleton } from "@/components/ui/skeleton";
import { api } from "@/lib/api/client";
import { unwrap } from "@/lib/api/problem";
import type { UserAccount } from "@/lib/api/types";
import { ROLES, type Role } from "@/lib/enums";
import { formatDateTime } from "@/lib/format";

interface Edit {
  user: UserAccount;
  displayName: string;
  role: Role;
  disabled: boolean;
  newPassword: string;
}

const KEY = ["admin", "users"] as const;

function RoleSelect({ id, value, onChange }: { id: string; value: Role; onChange: (role: Role) => void }) {
  return (
    <select id={id} className="h-9 w-full rounded-md border bg-background px-2 text-sm" value={value} onChange={(e) => onChange(e.target.value as Role)}>
      {ROLES.map((r) => (
        <option key={r} value={r}>
          {r}
        </option>
      ))}
    </select>
  );
}

/**
 * /admin/users: accounts and roles (ADR-011). Disabling an account signs it out everywhere (its refresh tokens are
 * revoked). The API refuses to disable or demote the last enabled admin, or your own account.
 */
export function UsersManager({ currentUserId }: { currentUserId: string | null }) {
  const queryClient = useQueryClient();
  const [creating, setCreating] = useState({ email: "", displayName: "", role: "Viewer" as Role, password: "" });
  const [editing, setEditing] = useState<Edit | null>(null);

  const users = useQuery({ queryKey: KEY, queryFn: async () => unwrap(await api.GET("/api/v1/admin/users")) });

  const create = useMutation({
    mutationFn: async () => unwrap(await api.POST("/api/v1/admin/users", { body: { ...creating, email: creating.email.trim(), displayName: creating.displayName.trim() } })),
    onSuccess: async () => {
      setCreating({ email: "", displayName: "", role: "Viewer", password: "" });
      await queryClient.invalidateQueries({ queryKey: KEY });
    },
  });

  const update = useMutation({
    mutationFn: async (e: Edit) =>
      unwrap(
        await api.PUT("/api/v1/admin/users/{id}", {
          params: { path: { id: e.user.id } },
          body: { displayName: e.displayName.trim(), role: e.role, disabled: e.disabled, newPassword: e.newPassword || null },
        }),
      ),
    onSuccess: async () => {
      setEditing(null);
      await queryClient.invalidateQueries({ queryKey: KEY });
    },
  });

  return (
    <div className="space-y-6">
      <Card>
        <CardHeader>
          <CardTitle className="flex items-center gap-2">
            <UserPlus className="size-4" aria-hidden /> New user
          </CardTitle>
          <CardDescription>Viewers read, Reviewers act on tenders and company documents, Admins configure everything. Passwords need 12 characters or more.</CardDescription>
        </CardHeader>
        <CardContent>
          <form
            className="grid gap-3 md:grid-cols-[minmax(0,1fr)_minmax(0,1fr)_9rem_minmax(0,1fr)_auto] md:items-end"
            onSubmit={(e) => {
              e.preventDefault();
              create.mutate();
            }}
          >
            <div className="space-y-1">
              <Label htmlFor="new-email">Email</Label>
              <Input id="new-email" type="email" required value={creating.email} onChange={(e) => setCreating({ ...creating, email: e.target.value })} />
            </div>
            <div className="space-y-1">
              <Label htmlFor="new-name">Display name</Label>
              <Input id="new-name" required value={creating.displayName} onChange={(e) => setCreating({ ...creating, displayName: e.target.value })} />
            </div>
            <div className="space-y-1">
              <Label htmlFor="new-role">Role</Label>
              <RoleSelect id="new-role" value={creating.role} onChange={(role) => setCreating({ ...creating, role })} />
            </div>
            <div className="space-y-1">
              <Label htmlFor="new-password">Password</Label>
              <Input id="new-password" type="password" autoComplete="new-password" required minLength={12} value={creating.password} onChange={(e) => setCreating({ ...creating, password: e.target.value })} />
            </div>
            <Button type="submit" disabled={create.isPending}>
              {create.isPending ? "Creating…" : "Create"}
            </Button>
          </form>
          {create.isError ? (
            <p role="alert" className="mt-2 text-sm text-destructive">
              {create.error.message}
            </p>
          ) : create.isSuccess ? (
            <p role="status" className="mt-2 text-sm text-muted-foreground">
              Account created.
            </p>
          ) : null}
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle>Accounts</CardTitle>
        </CardHeader>
        <CardContent>
          {users.isPending ? (
            <Skeleton className="h-32 w-full" />
          ) : users.isError ? (
            <p role="alert" className="text-sm text-destructive">
              {users.error.message}
            </p>
          ) : (
            <ul className="divide-y rounded-md border" aria-label="User accounts">
              {users.data.map((u) => (
                <li key={u.id} className="flex flex-col gap-2 p-3 text-sm sm:flex-row sm:items-center sm:justify-between" data-testid="user-row">
                  <div className="min-w-0 space-y-1">
                    <div className="flex flex-wrap items-center gap-2">
                      <span className="font-medium">{u.displayName}</span>
                      <span className="text-muted-foreground">{u.email}</span>
                      {u.id === currentUserId ? <Badge variant="outline">You</Badge> : null}
                    </div>
                    <div className="flex flex-wrap items-center gap-2 text-xs text-muted-foreground">
                      {u.roles.map((r) => (
                        <Badge key={r} variant={r === "Admin" ? "default" : "secondary"}>
                          {r}
                        </Badge>
                      ))}
                      {u.disabled ? <Badge variant="destructive">Disabled</Badge> : null}
                      {u.lockedOutUntil ? <Badge variant="outline">Locked until {formatDateTime(u.lockedOutUntil)}</Badge> : null}
                      <span>Last sign-in {u.lastLoginAt ? formatDateTime(u.lastLoginAt) : "never"}</span>
                    </div>
                  </div>
                  <Button
                    size="sm"
                    variant="outline"
                    onClick={() => {
                      update.reset();
                      setEditing({ user: u, displayName: u.displayName, role: (u.roles[0] as Role) ?? "Viewer", disabled: u.disabled, newPassword: "" });
                    }}
                  >
                    <Pencil /> Edit<span className="sr-only"> {u.email}</span>
                  </Button>
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
                <DialogTitle>Edit {editing.user.email}</DialogTitle>
                <DialogDescription>Leave the password empty to keep it.</DialogDescription>
              </DialogHeader>
              <div className="space-y-1">
                <Label htmlFor="edit-name">Display name</Label>
                <Input id="edit-name" required value={editing.displayName} onChange={(e) => setEditing({ ...editing, displayName: e.target.value })} />
              </div>
              <div className="space-y-1">
                <Label htmlFor="edit-role">Role</Label>
                <RoleSelect id="edit-role" value={editing.role} onChange={(role) => setEditing({ ...editing, role })} />
              </div>
              <div className="space-y-1">
                <Label htmlFor="edit-password">New password</Label>
                <Input id="edit-password" type="password" autoComplete="new-password" minLength={12} value={editing.newPassword} onChange={(e) => setEditing({ ...editing, newPassword: e.target.value })} />
              </div>
              <label className="flex items-center gap-2 text-sm">
                <input type="checkbox" checked={editing.disabled} onChange={(e) => setEditing({ ...editing, disabled: e.target.checked })} />
                Disabled (cannot sign in; current sessions end)
              </label>
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
    </div>
  );
}
