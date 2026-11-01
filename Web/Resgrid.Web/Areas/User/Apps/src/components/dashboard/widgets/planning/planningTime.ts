import { parseUtc, safeTimeZone, uiLocale } from '../../i18n';
import type { Translate, WidgetFormat } from '../types';

// Department-local day helpers shared by the planning widgets (agenda, shifts). Times arrive as UTC; days are grouped
// and labelled in the department's zone (ctx.format.timeZone), never the browser's.

const dayKeyFormats = new Map<string, Intl.DateTimeFormat>();
const dayLabelFormats = new Map<string, Intl.DateTimeFormat>();

function zoneKey(timeZone: string | undefined): string {
  return timeZone ?? '';
}

/** "YYYY-MM-DD" of the instant in the zone (null when the value is not a date). */
export function localDayKey(value: string | Date | null | undefined, timeZone: string | undefined): string | null {
  const date = parseUtc(value ?? null);
  if (!date) {
    return null;
  }

  const zone = safeTimeZone(timeZone);
  let format = dayKeyFormats.get(zoneKey(zone));
  if (!format) {
    format = new Intl.DateTimeFormat('en-CA', { timeZone: zone, year: 'numeric', month: '2-digit', day: '2-digit' });
    dayKeyFormats.set(zoneKey(zone), format);
  }
  return format.format(date);
}

/** "Today", "Tomorrow" (from the widget's strings) or a short weekday and date ("Tue, Nov 3") in the zone. */
export function dayLabel(
  value: string | Date | null | undefined,
  format: WidgetFormat,
  t: Translate,
  todayKey: string,
  tomorrowKey: string,
  compact = false,
): string {
  const date = parseUtc(value ?? null);
  if (!date) {
    return '—';
  }

  const day = localDayKey(date, format.timeZone);
  const now = new Date();
  if (day === localDayKey(now, format.timeZone)) {
    return t(todayKey);
  }
  if (day === localDayKey(new Date(now.getTime() + 24 * 3600 * 1000), format.timeZone)) {
    return t(tomorrowKey);
  }

  // Compact (the agenda's narrow day column): "Tue 3" within the coming week, "Nov 9" beyond it.
  const zone = safeTimeZone(format.timeZone);
  const style = !compact ? 'full' : date.getTime() - now.getTime() < 6 * 24 * 3600 * 1000 ? 'weekday' : 'date';
  const get = (key: string, options: Intl.DateTimeFormatOptions) => {
    const cacheKey = `${zoneKey(zone)}|${key}`;
    let labelFormat = dayLabelFormats.get(cacheKey);
    if (!labelFormat) {
      labelFormat = new Intl.DateTimeFormat(uiLocale(), { timeZone: zone, ...options });
      dayLabelFormats.set(cacheKey, labelFormat);
    }
    return labelFormat;
  };

  if (style === 'weekday') {
    // Composed so every locale reads weekday first ("Tue 3"); Intl orders weekday+day oddly in some (en-US: "3 Tue").
    return `${get('wd', { weekday: 'short' }).format(date)} ${get('d', { day: 'numeric' }).format(date)}`;
  }
  return style === 'full'
    ? get('full', { weekday: 'short', month: 'short', day: 'numeric' }).format(date)
    : get('md', { month: 'short', day: 'numeric' }).format(date);
}

/** A calendar day key ("YYYY-MM-DD", already department-local) as a short weekday and date. */
export function calendarDayLabel(dayKey: string, t: Translate, timeZone: string | undefined, todayKey: string, tomorrowKey: string): string {
  const today = localDayKey(new Date(), timeZone);
  if (dayKey === today) {
    return t(todayKey);
  }
  if (dayKey === localDayKey(new Date(Date.now() + 24 * 3600 * 1000), timeZone)) {
    return t(tomorrowKey);
  }

  const [year, month, day] = dayKey.split('-').map((part) => Number(part));
  if (!year || !month || !day) {
    return dayKey;
  }
  // Noon UTC keeps the calendar date stable whatever the formatter's zone.
  return new Intl.DateTimeFormat(uiLocale(), { timeZone: 'UTC', weekday: 'short', month: 'short', day: 'numeric' }).format(new Date(Date.UTC(year, month - 1, day, 12)));
}

/** "08:00–20:00" in the department's zone. */
export function timeRange(format: WidgetFormat, startUtc: string | null | undefined, endUtc: string | null | undefined): string {
  return `${format.time(startUtc)}–${format.time(endUtc)}`;
}
