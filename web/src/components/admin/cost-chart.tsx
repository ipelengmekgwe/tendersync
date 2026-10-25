"use client";

import { useEffect, useRef, useState } from "react";

export interface DayCost {
  /** SAST calendar day, yyyy-mm-dd. */
  day: string;
  costUsd: number;
  calls: number;
  failed: number;
}

const usd = new Intl.NumberFormat("en-ZA", { style: "currency", currency: "USD", minimumFractionDigits: 2, maximumFractionDigits: 2 });
const shortDay = new Intl.DateTimeFormat("en-ZA", { day: "numeric", month: "short", timeZone: "UTC" });

/** One entry per SAST day from `from` to `to` inclusive, zero where the API returned no bucket (it omits empty days). */
export function fillDays(buckets: { key: string; costUsd: number; calls: number; failed: number }[], from: string, to: string): DayCost[] {
  const byKey = new Map(buckets.map((b) => [b.key, b]));
  const sastDay = (iso: string) => new Intl.DateTimeFormat("en-CA", { timeZone: "Africa/Johannesburg", year: "numeric", month: "2-digit", day: "2-digit" }).format(new Date(iso));
  const days: DayCost[] = [];
  const cursor = new Date(`${sastDay(from)}T00:00:00Z`);
  const end = new Date(`${sastDay(to)}T00:00:00Z`);
  while (cursor <= end && days.length < 400) {
    const key = cursor.toISOString().slice(0, 10);
    const b = byKey.get(key);
    days.push({ day: key, costUsd: b?.costUsd ?? 0, calls: b?.calls ?? 0, failed: b?.failed ?? 0 });
    cursor.setUTCDate(cursor.getUTCDate() + 1);
  }
  return days;
}

/** Round the axis maximum up to 1, 2 or 5 × 10^n so gridlines land on readable values. */
export function niceMax(value: number): number {
  if (value <= 0) {
    return 1;
  }
  const power = 10 ** Math.floor(Math.log10(value));
  const step = [1, 2, 5, 10].find((m) => m * power >= value) ?? 10;
  return step * power;
}

const HEIGHT = 180;
const PAD = { top: 12, right: 8, bottom: 24, left: 48 };

function barPath(x: number, y: number, w: number, h: number): string {
  // 4px rounded data end (top), square at the baseline.
  const r = Math.min(4, w / 2, h);
  return `M${x},${y + h} V${y + r} Q${x},${y} ${x + r},${y} H${x + w - r} Q${x + w},${y} ${x + w},${y + r} V${y + h} Z`;
}

/**
 * Model spend per day (one series, so no legend: the card title names it). Bars grow from one baseline, are at most
 * 24px wide with a 2px gap, and every bar answers hover and keyboard focus with its day, cost and calls. A table view
 * sits under the chart for screen readers and exact values.
 */
export function CostChart({ days }: { days: DayCost[] }) {
  const ref = useRef<HTMLDivElement>(null);
  const [width, setWidth] = useState(600);
  const [active, setActive] = useState<number | null>(null);

  useEffect(() => {
    const element = ref.current;
    if (!element) {
      return;
    }
    const observer = new ResizeObserver(([entry]) => setWidth(Math.max(260, entry.contentRect.width)));
    observer.observe(element);
    return () => observer.disconnect();
  }, []);

  const max = niceMax(Math.max(0, ...days.map((d) => d.costUsd)));
  const plotW = width - PAD.left - PAD.right;
  const plotH = HEIGHT - PAD.top - PAD.bottom;
  const slot = days.length > 0 ? plotW / days.length : plotW;
  const barW = Math.max(2, Math.min(24, slot - 2));
  const ticks = [0, 0.5, 1].map((f) => f * max);
  const labelEvery = Math.max(1, Math.ceil(days.length / Math.max(2, Math.floor(plotW / 56))));
  const total = days.reduce((sum, d) => sum + d.costUsd, 0);
  const current = active !== null ? days[active] : null;

  return (
    <figure className="space-y-2">
      <div ref={ref} className="relative" onMouseLeave={() => setActive(null)}>
        <svg width={width} height={HEIGHT} role="img" aria-label={`Model cost per day over ${days.length} days, ${usd.format(total)} in total. Exact values are in the table below.`}>
          {ticks.map((t) => {
            const y = PAD.top + plotH - (t / max) * plotH;
            return (
              <g key={t}>
                <line x1={PAD.left} x2={width - PAD.right} y1={y} y2={y} className="stroke-border" strokeWidth={1} strokeDasharray={t === 0 ? undefined : "2 4"} />
                <text x={PAD.left - 6} y={y} dy="0.32em" textAnchor="end" className="fill-muted-foreground text-[10px] tabular-nums">
                  {usd.format(t)}
                </text>
              </g>
            );
          })}
          {days.map((d, i) => {
            const h = (d.costUsd / max) * plotH;
            const x = PAD.left + i * slot + (slot - barW) / 2;
            const y = PAD.top + plotH - h;
            return (
              <g
                key={d.day}
                tabIndex={0}
                role="button"
                aria-label={`${shortDay.format(new Date(`${d.day}T00:00:00Z`))}: ${usd.format(d.costUsd)}, ${d.calls} call${d.calls === 1 ? "" : "s"}`}
                className="cursor-default outline-none [&:focus-visible>rect:first-child]:stroke-ring"
                onMouseEnter={() => setActive(i)}
                onFocus={() => setActive(i)}
                onBlur={() => setActive(null)}
              >
                {/* Hit target: the whole slot, taller than the bar. */}
                <rect x={PAD.left + i * slot} y={PAD.top} width={slot} height={plotH} fill="transparent" strokeWidth={2} />
                {h > 0 ? <path d={barPath(x, y, barW, h)} fill="var(--viz-series)" opacity={active === null || active === i ? 1 : 0.55} /> : null}
                {i % labelEvery === 0 ? (
                  <text x={PAD.left + i * slot + slot / 2} y={HEIGHT - 6} textAnchor="middle" className="fill-muted-foreground text-[10px]">
                    {shortDay.format(new Date(`${d.day}T00:00:00Z`))}
                  </text>
                ) : null}
              </g>
            );
          })}
        </svg>
        {current && active !== null ? (
          <div
            role="status"
            className="pointer-events-none absolute top-0 z-10 rounded-md border bg-popover px-2 py-1 text-xs text-popover-foreground shadow-sm"
            style={{ left: Math.min(Math.max(0, PAD.left + active * slot + slot / 2 - 70), width - 140), width: 140 }}
          >
            <div className="font-medium">{shortDay.format(new Date(`${current.day}T00:00:00Z`))}</div>
            <div className="tabular-nums">{usd.format(current.costUsd)}</div>
            <div className="text-muted-foreground">
              {current.calls} call{current.calls === 1 ? "" : "s"}
              {current.failed > 0 ? `, ${current.failed} failed` : ""}
            </div>
          </div>
        ) : null}
      </div>
      <details>
        <summary className="cursor-pointer text-xs text-muted-foreground">Show as table</summary>
        <div className="mt-2 max-h-64 overflow-auto">
          <table className="w-full text-left text-xs">
            <thead className="text-muted-foreground">
              <tr>
                <th className="py-1 pr-3 font-medium">Day</th>
                <th className="py-1 pr-3 font-medium">Cost</th>
                <th className="py-1 pr-3 font-medium">Calls</th>
                <th className="py-1 font-medium">Failed</th>
              </tr>
            </thead>
            <tbody>
              {[...days].reverse().map((d) => (
                <tr key={d.day} className="border-t">
                  <td className="py-1 pr-3">{d.day}</td>
                  <td className="py-1 pr-3 tabular-nums">{usd.format(d.costUsd)}</td>
                  <td className="py-1 pr-3 tabular-nums">{d.calls}</td>
                  <td className="py-1 tabular-nums">{d.failed}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </details>
    </figure>
  );
}
