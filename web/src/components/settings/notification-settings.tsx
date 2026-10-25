"use client";

import { useEffect, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Send } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Skeleton } from "@/components/ui/skeleton";
import { api } from "@/lib/api/client";
import { unwrap } from "@/lib/api/problem";
import type { NotificationPreference, NotificationPreferenceInput } from "@/lib/api/types";
import { NotificationChannel, NotificationStatus, NotificationType, label } from "@/lib/enums";
import { formatDateTime } from "@/lib/format";

interface ChannelDraft {
  channel: number;
  address: string;
  enabled: boolean;
  types: number[];
  quietStart: string;
  quietEnd: string;
}

const CHANNELS: { channel: number; placeholder: string; hint: string }[] = [
  { channel: 0, placeholder: "you@company.co.za", hint: "Email address" },
  { channel: 1, placeholder: "123456789", hint: "Numeric chat id from the TenderSync bot (or @channel)" },
  { channel: 2, placeholder: "https://…webhook.office.com/…", hint: "Teams incoming-webhook URL (https)" },
  { channel: 3, placeholder: "+27821234567", hint: "Mobile number in international format" },
  { channel: 4, placeholder: "+27821234567", hint: "WhatsApp number in international format" },
];

/** Types a person can subscribe to; Test messages always go through. */
const TYPES = Object.entries(NotificationType)
  .map(([value, name]) => ({ value: Number(value), name }))
  .filter((t) => t.value !== 11);

const DEFAULT_TYPES = [0, 1, 2, 3, 4, 5, 6, 7];

export function toDrafts(preferences: NotificationPreference[]): ChannelDraft[] {
  return CHANNELS.map(({ channel }) => {
    const p = preferences.find((x) => x.channel === channel);
    return {
      channel,
      address: p?.address ?? "",
      enabled: p?.enabled ?? false,
      types: p ? [...p.types] : DEFAULT_TYPES,
      quietStart: p?.quietHours?.start?.slice(0, 5) ?? "",
      quietEnd: p?.quietHours?.end?.slice(0, 5) ?? "",
    };
  });
}

/** Channels without an address are left out, which removes them (PUT replaces the caller's subscriptions). */
export function toInputs(drafts: ChannelDraft[]): NotificationPreferenceInput[] {
  return drafts
    .filter((d) => d.address.trim() !== "")
    .map((d) => ({
      channel: d.channel,
      address: d.address.trim(),
      enabled: d.enabled,
      types: d.types,
      quietHours: d.quietStart && d.quietEnd ? { start: d.quietStart, end: d.quietEnd, timeZone: "Africa/Johannesburg" } : null,
    }));
}

const PREFS_KEY = ["notification-preferences"] as const;

/**
 * /settings/notifications: one row per channel (address, on/off, which messages, quiet hours) and a test send. Test
 * messages are written by the API and delivered by the Worker's sweeper, usually within a minute.
 */
export function NotificationSettings() {
  const queryClient = useQueryClient();
  const [drafts, setDrafts] = useState<ChannelDraft[] | null>(null);
  const [testResult, setTestResult] = useState<string | null>(null);

  const preferences = useQuery({ queryKey: PREFS_KEY, queryFn: async () => unwrap(await api.GET("/api/v1/notification-preferences")) });
  const recent = useQuery({
    queryKey: ["notifications", "mine"],
    queryFn: async () => unwrap(await api.GET("/api/v1/notifications", { params: { query: { pageSize: 10 } } })),
  });

  useEffect(() => {
    if (preferences.data) {
      setDrafts(toDrafts(preferences.data));
    }
  }, [preferences.data]);

  const save = useMutation({
    mutationFn: async (all: ChannelDraft[]) => unwrap(await api.PUT("/api/v1/notification-preferences", { body: toInputs(all) })),
    onSuccess: (saved) => {
      queryClient.setQueryData(PREFS_KEY, saved);
    },
  });

  const test = useMutation({
    mutationFn: async (channel: number) => unwrap(await api.POST("/api/v1/notifications/test", { body: { channel } })),
    onSuccess: async (_, channel) => {
      setTestResult(`Test message for ${label(NotificationChannel, channel)} queued. The Worker sends it within about a minute.`);
      await queryClient.invalidateQueries({ queryKey: ["notifications", "mine"] });
    },
    onError: (error) => setTestResult(error.message),
  });

  const update = (channel: number, patch: Partial<ChannelDraft>) =>
    setDrafts((current) => current?.map((d) => (d.channel === channel ? { ...d, ...patch } : d)) ?? null);

  const savedChannels = new Set((preferences.data ?? []).map((p) => p.channel));

  return (
    <div className="space-y-6">
      <Card>
        <CardHeader>
          <CardTitle>Channels</CardTitle>
          <CardDescription>
            Where your alerts go. Leave an address empty to remove that channel. Quiet hours hold non-urgent messages until they end (South African time).
            Bid-pack contact details are never sent through any channel.
          </CardDescription>
        </CardHeader>
        <CardContent>
          {preferences.isPending || !drafts ? (
            <Skeleton className="h-64 w-full" />
          ) : preferences.isError ? (
            <p role="alert" className="text-sm text-destructive">
              {preferences.error.message}
            </p>
          ) : (
            <form
              className="space-y-4"
              onSubmit={(e) => {
                e.preventDefault();
                save.mutate(drafts);
              }}
            >
              {drafts.map((d) => {
                const meta = CHANNELS.find((c) => c.channel === d.channel)!;
                const name = label(NotificationChannel, d.channel);
                const base = `channel-${d.channel}`;
                return (
                  <fieldset key={d.channel} className="space-y-3 rounded-md border p-3" data-testid={`channel-${name}`}>
                    <legend className="px-1 text-sm font-medium">{name}</legend>
                    <div className="grid gap-3 md:grid-cols-[minmax(0,1fr)_auto_auto] md:items-end">
                      <div className="space-y-1">
                        <Label htmlFor={`${base}-address`}>{meta.hint}</Label>
                        <Input id={`${base}-address`} value={d.address} placeholder={meta.placeholder} onChange={(e) => update(d.channel, { address: e.target.value })} />
                      </div>
                      <label className="flex items-center gap-2 pb-2 text-sm">
                        <input type="checkbox" checked={d.enabled} onChange={(e) => update(d.channel, { enabled: e.target.checked })} />
                        On
                      </label>
                      <Button
                        type="button"
                        variant="outline"
                        size="sm"
                        disabled={!savedChannels.has(d.channel) || test.isPending}
                        title={savedChannels.has(d.channel) ? undefined : "Save the channel first"}
                        onClick={() => {
                          setTestResult(null);
                          test.mutate(d.channel);
                        }}
                      >
                        <Send /> Send test
                      </Button>
                    </div>
                    <details>
                      <summary className="cursor-pointer text-sm text-muted-foreground">
                        Messages ({d.types.length}) and quiet hours{d.quietStart && d.quietEnd ? ` ${d.quietStart}–${d.quietEnd}` : ""}
                      </summary>
                      <div className="mt-2 grid gap-1 sm:grid-cols-2 lg:grid-cols-3">
                        {TYPES.map((t) => (
                          <label key={t.value} className="flex items-center gap-2 text-sm">
                            <input
                              type="checkbox"
                              checked={d.types.includes(t.value)}
                              onChange={(e) =>
                                update(d.channel, { types: e.target.checked ? [...d.types, t.value].sort((a, b) => a - b) : d.types.filter((x) => x !== t.value) })
                              }
                            />
                            {t.name}
                          </label>
                        ))}
                      </div>
                      <div className="mt-3 flex flex-wrap items-end gap-3">
                        <div className="space-y-1">
                          <Label htmlFor={`${base}-quiet-start`}>Quiet from</Label>
                          <Input id={`${base}-quiet-start`} type="time" className="w-32" value={d.quietStart} onChange={(e) => update(d.channel, { quietStart: e.target.value })} />
                        </div>
                        <div className="space-y-1">
                          <Label htmlFor={`${base}-quiet-end`}>until</Label>
                          <Input id={`${base}-quiet-end`} type="time" className="w-32" value={d.quietEnd} onChange={(e) => update(d.channel, { quietEnd: e.target.value })} />
                        </div>
                      </div>
                    </details>
                  </fieldset>
                );
              })}
              {save.isError ? (
                <p role="alert" className="text-sm text-destructive">
                  {save.error.message}
                </p>
              ) : save.isSuccess ? (
                <p role="status" className="text-sm text-muted-foreground">
                  Saved.
                </p>
              ) : null}
              {testResult ? (
                <p role="status" className="text-sm">
                  {testResult}
                </p>
              ) : null}
              <Button type="submit" disabled={save.isPending}>
                {save.isPending ? "Saving…" : "Save channels"}
              </Button>
            </form>
          )}
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle>Recent messages to you</CardTitle>
          <CardDescription>The last ten notifications addressed to your channels.</CardDescription>
        </CardHeader>
        <CardContent>
          {recent.isPending ? (
            <Skeleton className="h-24 w-full" />
          ) : recent.isError ? (
            <p role="alert" className="text-sm text-destructive">
              {recent.error.message}
            </p>
          ) : recent.data.items.length === 0 ? (
            <p className="text-sm text-muted-foreground">Nothing sent yet.</p>
          ) : (
            <ul className="divide-y text-sm">
              {recent.data.items.map((n) => (
                <li key={n.id} className="flex flex-col gap-1 py-2 sm:flex-row sm:items-center sm:justify-between">
                  <div className="min-w-0">
                    <div className="truncate font-medium">{n.subject}</div>
                    <div className="text-xs text-muted-foreground">
                      {label(NotificationType, n.type)} · {label(NotificationChannel, n.channel)} to {n.recipient} · {formatDateTime(n.sentAt ?? n.createdAt)}
                    </div>
                  </div>
                  <Badge variant={n.status === 1 ? "secondary" : n.status === 2 ? "destructive" : "outline"} title={n.error ?? undefined}>
                    {label(NotificationStatus, n.status)}
                  </Badge>
                </li>
              ))}
            </ul>
          )}
        </CardContent>
      </Card>
    </div>
  );
}
