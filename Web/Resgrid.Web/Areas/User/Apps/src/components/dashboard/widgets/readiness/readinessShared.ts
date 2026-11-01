import { uiLocale } from '../../i18n';
import type { Tone } from '../../primitives';
import type { SizeToken } from '../types';

// Helpers shared by the readiness and records widgets (agent C slice).

/** The server's semantic tone ("danger" / "warning" / "info" / "default") as a primitive Tone (icon + accessible color). */
export function toTone(tone: string | null | undefined): Tone | undefined {
  switch (tone) {
    case 'danger':
    case 'bad':
      return 'bad';
    case 'warning':
    case 'warn':
      return 'warn';
    case 'info':
      return 'info';
    case 'success':
    case 'good':
      return 'good';
    default:
      return undefined;
  }
}

/** xs / s show the headline only and fewer rows. */
export function isCompact(size: SizeToken): boolean {
  return size === 'xs' || size === 's';
}

/** The rows a size has room for: the server already applied maxRows; compact sizes show at most `compactRows`. */
export function rowsFor<T>(items: T[] | null | undefined, size: SizeToken, compactRows = 3): T[] {
  const list = items ?? [];
  return isCompact(size) ? list.slice(0, compactRows) : list;
}

export function enc(value: string | number | null | undefined): string {
  return encodeURIComponent(value === null || value === undefined ? '' : String(value));
}

/** A tone only when the count is above zero (a zero is not a warning). */
export function toneIf(count: number | null | undefined, tone: Tone): Tone | undefined {
  return count && count > 0 ? tone : undefined;
}

/** Joins the non-empty parts with a middle dot. */
export function joinMeta(...parts: Array<string | null | undefined | false>): string {
  return parts.filter((part): part is string => typeof part === 'string' && part.length > 0).join(' · ');
}

/** Milliseconds since the epoch for a server UTC value (zone-less values are UTC), or null. */
export function utcTime(utc: string | null | undefined): number | null {
  if (!utc) {
    return null;
  }
  const text = /(?:Z|[+-]\d{2}:?\d{2})$/i.test(utc) || !/T\d/.test(utc) ? utc : `${utc}Z`;
  const time = new Date(text).getTime();
  return Number.isNaN(time) ? null : time;
}

/** True when `utc` is at or before now. */
export function isPast(utc: string | null | undefined): boolean {
  const time = utcTime(utc);
  return time !== null && time <= Date.now();
}

let dayFormat: Intl.DateTimeFormat | null = null;

/**
 * A calendar day the server already expressed in the department's zone (a date with no time, e.g. a trend bucket):
 * formatted as that day, never shifted through a time zone.
 */
export function calendarDay(day: string | null | undefined): string {
  const match = /^(\d{4})-(\d{2})-(\d{2})/.exec(day ?? '');
  if (!match) {
    return '—';
  }
  dayFormat ??= new Intl.DateTimeFormat(uiLocale(), { month: 'short', day: 'numeric', timeZone: 'UTC' });
  return dayFormat.format(new Date(Date.UTC(Number(match[1]), Number(match[2]) - 1, Number(match[3]))));
}

/** Whole days from now until `utc` (negative in the past), or null. */
export function daysFromNow(utc: string | null | undefined): number | null {
  const time = utcTime(utc);
  return time === null ? null : Math.round((time - Date.now()) / 86400000);
}

/** Expired / expires today / expires in N days (certifications widgets; `prefix` is the widget's string prefix). */
export function expiryText(t: (key: string, ...args: Array<string | number>) => string, prefix: string, days: number | null | undefined): string | null {
  if (days === null || days === undefined) {
    return null;
  }
  if (days < 0) return t(`${prefix}ExpiredDaysAgo`, -days);
  if (days === 0) return t(`${prefix}ExpiresToday`);
  return t(`${prefix}ExpiresInDays`, days);
}
