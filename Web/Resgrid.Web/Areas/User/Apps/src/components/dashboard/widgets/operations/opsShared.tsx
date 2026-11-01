import { useEffect, useRef, useState, type CSSProperties } from 'react';
import { parseUtc } from '../../i18n';
import { StatusChip } from '../../primitives';
import type { SizeToken } from '../types';

// Types and small pieces the operations and personnel widgets share (agent A). The DTO shapes mirror
// Core/Resgrid.Model/Dashboard/Widgets/Operations/DashboardStatusChip.cs.

/** DashboardStatusChip: a unit status, personnel status or staffing level with the department's colours. */
export interface StatusChipData {
  Id: number;
  Text?: string | null;
  /** Background, "#RRGGBB". */
  Color?: string | null;
  TextColor?: string | null;
  /** ActionBaseTypes (statuses) / StaffingBaseTypes (staffing); -1 none. */
  BaseType: number;
  /** AvailabilityClass: 0 unknown, 1 available, 2 committed, 3 unavailable, 4 delayed. */
  Availability: number;
  IsCustom: boolean;
}

/** DashboardCallPriority. */
export interface CallPriorityData {
  Id: number;
  Name?: string | null;
  Color?: string | null;
}

/** DashboardWidgetEndpoint: an action the widget calls. */
export interface EndpointData {
  Url: string;
  Method: string;
  /** "query" | "form" | "json". */
  Body?: string | null;
  Parameters?: string[] | null;
  Available: boolean;
}

export const Availability = { Unknown: 0, Available: 1, Committed: 2, Unavailable: 3, Delayed: 4 } as const;

export function isCompact(size: SizeToken): boolean {
  return size === 'xs' || size === 's';
}

export function isWide(size: SizeToken): boolean {
  return size === 'xl' || size === 'full';
}

export function enc(value: string | number): string {
  return encodeURIComponent(String(value));
}

/** A DTO endpoint URL under /api/web-bff/ as the path ctx.api expects (relative to the BFF). */
export function bffPath(url: string, id?: string): string {
  const withId = id === undefined ? url : url.replace('{id}', enc(id));
  return withId.replace(/^\/?api\/web-bff\//i, '');
}

/** Minutes since a UTC time (0 when unknown or in the future). */
export function minutesSince(utc: string | null | undefined, now: number = Date.now()): number {
  const date = parseUtc(utc);
  if (!date) {
    return 0;
  }
  return Math.max(0, Math.floor((now - date.getTime()) / 60000));
}

/** Re-renders every `intervalMs` so ages and countdowns stay current between data refreshes. */
export function useNow(intervalMs = 30000): number {
  const [now, setNow] = useState(() => Date.now());
  useEffect(() => {
    const timer = window.setInterval(() => setNow(Date.now()), intervalMs);
    return () => window.clearInterval(timer);
  }, [intervalMs]);
  return now;
}

/**
 * Keys that were not in the previous render's set (a new call, a changed row). Empty on the first render, so a page
 * load does not flash every row.
 */
export function useNewKeys(keys: string[]): Set<string> {
  const previous = useRef<Set<string> | null>(null);
  const joined = keys.join('|');
  const [fresh, setFresh] = useState<Set<string>>(() => new Set());

  useEffect(() => {
    const current = new Set(joined ? joined.split('|') : []);
    if (previous.current) {
      const added = new Set([...current].filter((key) => !previous.current!.has(key)));
      setFresh(added);
    }
    previous.current = current;
  }, [joined]);

  return fresh;
}

/** The status chip as a coloured dot (lists, tables) or the department's solid button look. */
export function Chip({ chip, solid, fallback }: { chip: StatusChipData | null | undefined; solid?: boolean; fallback?: string }) {
  if (!chip) {
    return fallback ? <span className="rg-mu">{fallback}</span> : null;
  }
  return <StatusChip label={chip.Text || fallback || ''} color={chip.Color} textColor={chip.TextColor} variant={solid ? 'solid' : 'dot'} />;
}

/** A call priority: the department's colour as a swatch beside the priority's name (never colour alone). */
export function PriorityLabel({ priority }: { priority: CallPriorityData | null | undefined }) {
  if (!priority) {
    return null;
  }
  return (
    <span className="rg-pri" style={{ '--pri': priority.Color || 'var(--rg-muted)' } as CSSProperties}>
      {priority.Name}
    </span>
  );
}

/** Per-viewer collapsed groups for a widget instance (a convenience; browser storage may be unavailable). */
export function useCollapsedGroups(instanceId: string): [string[], (key: string) => void] {
  const storageKey = `rg-dash-collapsed:${instanceId}`;
  const [collapsed, setCollapsed] = useState<string[]>(() => {
    try {
      const raw = window.localStorage.getItem(storageKey);
      const parsed = raw ? (JSON.parse(raw) as unknown) : [];
      return Array.isArray(parsed) ? parsed.filter((x): x is string => typeof x === 'string') : [];
    } catch {
      return [];
    }
  });

  const toggle = (key: string) => {
    setCollapsed((current) => {
      const next = current.includes(key) ? current.filter((k) => k !== key) : [...current, key];
      try {
        window.localStorage.setItem(storageKey, JSON.stringify(next));
      } catch {
        // Private window or blocked storage: the toggle still works for this page view.
      }
      return next;
    });
  };

  return [collapsed, toggle];
}

/** The body of a JSON reply from the classic MVC actions ({ success, message }). */
export function replyOf(json: unknown): { success?: boolean; message?: string; noRecipients?: boolean; resolved?: number } {
  return json && typeof json === 'object' ? (json as { success?: boolean; message?: string }) : {};
}
