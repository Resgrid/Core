import chromeDefaults from './chromeStrings.en.json';

// Strings come from the HomeDashboard (chrome) and DashboardWidgets (W.<key>.<Name>) resx families, serialized by
// DashboardModern.cshtml into the element's `strings` attribute. The English defaults below only cover the gap until a
// key reaches the resx files (and keep a static harness readable); a resx value always wins.

/** English fallbacks for chrome keys that already exist in HomeDashboard.en.resx. */
const existingDefaults: Record<string, string> = {
  DashboardPageTitle: 'Dashboard',
  WidgetErrorFailed: "This widget couldn't load. Try again in a moment.",
  WidgetErrorForbidden: 'You no longer have access to this widget.',
  WidgetErrorNotFound: 'This widget is no longer available.',
  WidgetErrorProtected: 'Protected data. Verify to view.',
  SaveErrorConflict: 'Your dashboard was changed in another window.',
  SaveErrorInvalid: "That layout couldn't be saved.",
  SaveErrorTooLarge: 'That layout is too large to save. Remove a few widgets and try again.',
  SaveErrorLimitReached: 'You have reached the limit.',
  SaveErrorNotFound: 'That board no longer exists.',
  SaveErrorNotAllowed: "You can't change this dashboard.",
  BoardHome: 'Home',
  TemplatesTitle: 'Dashboard templates',
  TemplateAudienceEveryone: 'Everyone',
  TemplateAudienceRole: 'Personnel role',
  TemplateAudienceGroup: 'Group',
  TemplateName: 'Name',
  TemplateAudience: 'For',
  TemplatePriority: 'Priority',
  TemplateDefault: 'Department default',
  UseClassicDashboard: 'Use the classic dashboard',
};

const defaults: Record<string, string> = { ...existingDefaults, ...(chromeDefaults as Record<string, string>) };

let dictionary: Record<string, string> = {};

export function setStrings(strings: Record<string, string> | null | undefined): void {
  dictionary = strings && typeof strings === 'object' ? strings : {};
}

/** Localized text for `key`, with {0}, {1}, … replaced by `args`. Falls back to the English default, then the key. */
export function t(key: string, ...args: Array<string | number>): string {
  const template = dictionary[key] ?? defaults[key] ?? key;
  return args.length === 0
    ? template
    : template.replace(/\{(\d+)\}/g, (match, index: string) => {
        const value = args[Number(index)];
        return value === undefined ? match : String(value);
      });
}

export function has(key: string): boolean {
  return Object.prototype.hasOwnProperty.call(dictionary, key) || Object.prototype.hasOwnProperty.call(defaults, key);
}

export type Translate = (key: string, ...args: Array<string | number>) => string;

let culture: string | undefined;

/** The host's UI culture (the element's culture attribute, CultureInfo.CurrentUICulture.Name); invalid tags are ignored. */
export function setCulture(value: string | null | undefined): void {
  const tag = value && value.trim() ? value.trim() : undefined;
  if (tag === culture) {
    return;
  }
  try {
    culture = tag ? Intl.getCanonicalLocales(tag)[0] : undefined;
  } catch {
    culture = undefined;
  }
}

/** The locale for every Intl formatter: the host's UI culture, otherwise the browser's language. */
export function uiLocale(): string | undefined {
  return culture || (typeof navigator !== 'undefined' && navigator.language ? navigator.language : undefined);
}

/** The IANA zone if Intl accepts it, otherwise undefined (the browser's zone). */
export function safeTimeZone(timeZone: string | null | undefined): string | undefined {
  if (!timeZone) {
    return undefined;
  }

  try {
    new Intl.DateTimeFormat('en-US', { timeZone }).format(0);
    return timeZone;
  } catch {
    return undefined;
  }
}

/** Parses a server DateTime. Values serialized without a zone designator are UTC (DTO rule: times are UTC). */
export function parseUtc(value: string | number | Date | null | undefined): Date | null {
  if (value === null || value === undefined || value === '') {
    return null;
  }

  if (value instanceof Date) {
    return Number.isNaN(value.getTime()) ? null : value;
  }

  if (typeof value === 'number') {
    return new Date(value);
  }

  const text = /(?:Z|[+-]\d{2}:?\d{2})$/i.test(value) || !/T\d/.test(value) ? value : `${value}Z`;
  const parsed = new Date(text);
  return Number.isNaN(parsed.getTime()) ? null : parsed;
}

/** Formatting helpers handed to widgets as ctx.format: one culture, one department time zone, everywhere. */
export interface WidgetFormat {
  /** The department's IANA time zone (or undefined for the browser's). */
  timeZone: string | undefined;
  number(value: number | null | undefined, maximumFractionDigits?: number): string;
  percent(value: number | null | undefined, maximumFractionDigits?: number): string;
  currency(value: number | null | undefined, currencyCode?: string): string;
  /** Department-local time of day, e.g. "18:42". */
  time(utc: string | Date | null | undefined): string;
  /** Department-local short date, e.g. "Oct 31". */
  date(utc: string | Date | null | undefined): string;
  /** Department-local short date and time. */
  dateTime(utc: string | Date | null | undefined): string;
  /** Relative age, e.g. "5 min. ago". */
  ago(utc: string | Date | number | null | undefined): string;
  /** A duration in minutes as "45m", "2h 05m" or "3d". */
  duration(minutes: number | null | undefined): string;
}

export function createFormat(timeZone: string | null | undefined): WidgetFormat {
  const zone = safeTimeZone(timeZone);
  const locale = uiLocale();
  const numberFormats = new Map<number, Intl.NumberFormat>();
  const numberFormat = (digits: number) => {
    let format = numberFormats.get(digits);
    if (!format) {
      format = new Intl.NumberFormat(locale, { maximumFractionDigits: digits });
      numberFormats.set(digits, format);
    }
    return format;
  };
  const timeFormat = new Intl.DateTimeFormat(locale, { timeZone: zone, hour: '2-digit', minute: '2-digit' });
  const dateFormat = new Intl.DateTimeFormat(locale, { timeZone: zone, month: 'short', day: 'numeric' });
  const dateTimeFormat = new Intl.DateTimeFormat(locale, { timeZone: zone, month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit' });
  const relative = new Intl.RelativeTimeFormat(locale, { numeric: 'auto', style: 'short' });
  const dash = '—';

  return {
    timeZone: zone,
    number: (value, digits = 1) => (value === null || value === undefined || !Number.isFinite(value) ? dash : numberFormat(digits).format(value)),
    percent: (value, digits = 0) =>
      value === null || value === undefined || !Number.isFinite(value)
        ? dash
        : new Intl.NumberFormat(locale, { style: 'percent', maximumFractionDigits: digits }).format(value / 100),
    currency: (value, currencyCode = 'USD') => {
      if (value === null || value === undefined || !Number.isFinite(value)) {
        return dash;
      }
      try {
        return new Intl.NumberFormat(locale, { style: 'currency', currency: currencyCode }).format(value);
      } catch {
        return numberFormat(2).format(value);
      }
    },
    time: (utc) => {
      const date = parseUtc(utc);
      return date ? timeFormat.format(date) : dash;
    },
    date: (utc) => {
      const date = parseUtc(utc);
      return date ? dateFormat.format(date) : dash;
    },
    dateTime: (utc) => {
      const date = parseUtc(utc);
      return date ? dateTimeFormat.format(date) : dash;
    },
    ago: (utc) => {
      const date = parseUtc(utc);
      if (!date) {
        return dash;
      }
      const seconds = Math.round((date.getTime() - Date.now()) / 1000);
      const abs = Math.abs(seconds);
      if (abs < 45) return relative.format(0, 'second');
      if (abs < 3600) return relative.format(Math.round(seconds / 60), 'minute');
      if (abs < 86400) return relative.format(Math.round(seconds / 3600), 'hour');
      return relative.format(Math.round(seconds / 86400), 'day');
    },
    duration: (minutes) => {
      if (minutes === null || minutes === undefined || !Number.isFinite(minutes)) {
        return dash;
      }
      const total = Math.max(0, Math.round(minutes));
      if (total < 60) return t('Dash.DurationMinutes', total);
      if (total < 1440) return t('Dash.DurationHours', Math.floor(total / 60), String(total % 60).padStart(2, '0'));
      return t('Dash.DurationDays', Math.floor(total / 1440));
    },
  };
}
