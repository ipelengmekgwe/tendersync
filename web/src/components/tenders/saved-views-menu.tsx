"use client";

import { usePathname, useRouter } from "next/navigation";
import { useEffect, useState } from "react";
import { Bookmark, Trash2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { type SavedView, readSavedViews, removeView, saveView, writeSavedViews } from "@/components/tenders/saved-views";

/** Pick, save and delete named views of the tender list. The current filters come in as the URL query string. */
export function SavedViewsMenu({ currentQuery }: { currentQuery: string }) {
  const router = useRouter();
  const pathname = usePathname();
  const [views, setViews] = useState<SavedView[]>([]);
  const [name, setName] = useState("");
  const [saving, setSaving] = useState(false);

  useEffect(() => setViews(readSavedViews()), []);

  function update(next: SavedView[]) {
    setViews(next);
    writeSavedViews(next);
  }

  const selected = views.find((v) => v.query === currentQuery)?.name ?? "";

  return (
    <div className="flex flex-wrap items-end gap-2 text-sm">
      <div className="space-y-1">
        <Label htmlFor="saved-view">Saved views</Label>
        <div className="flex gap-1">
          <select
            id="saved-view"
            className="h-8 max-w-56 rounded-md border bg-background px-2"
            value={selected}
            onChange={(e) => {
              const view = views.find((v) => v.name === e.target.value);
              if (view) {
                router.push(view.query ? `${pathname}?${view.query}` : pathname);
              }
            }}
          >
            <option value="">{views.length === 0 ? "None saved" : "Choose a view"}</option>
            {views.map((v) => (
              <option key={v.name} value={v.name}>
                {v.name}
              </option>
            ))}
          </select>
          {selected ? (
            <Button variant="ghost" size="icon-sm" aria-label={`Delete saved view ${selected}`} onClick={() => update(removeView(views, selected))}>
              <Trash2 />
            </Button>
          ) : null}
        </div>
      </div>
      {saving ? (
        <form
          className="flex items-end gap-1"
          onSubmit={(e) => {
            e.preventDefault();
            update(saveView(views, name, currentQuery));
            setName("");
            setSaving(false);
          }}
        >
          <div className="space-y-1">
            <Label htmlFor="saved-view-name">View name</Label>
            <Input id="saved-view-name" className="h-8 w-44" value={name} maxLength={60} autoFocus onChange={(e) => setName(e.target.value)} />
          </div>
          <Button type="submit" size="sm" disabled={!name.trim()}>
            Save
          </Button>
          <Button type="button" size="sm" variant="ghost" onClick={() => setSaving(false)}>
            Cancel
          </Button>
        </form>
      ) : (
        <Button variant="outline" size="sm" onClick={() => setSaving(true)}>
          <Bookmark /> Save this view
        </Button>
      )}
    </div>
  );
}
