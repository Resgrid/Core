import type { VizCategory, VizColorBy, VizOptions } from './vizTypes';

// Strings come from the MapVisualizations resx family, passed by the Razor page as the element's `strings` attribute.

let dictionary: Record<string, string> = {};

export function setStrings(strings: Record<string, string> | null | undefined): void {
  dictionary = strings ?? {};
}

/** Localized text for <paramref name="key"/>, with {0}, {1}, … replaced by <paramref name="args"/>. Falls back to the key. */
export function t(key: string, ...args: Array<string | number>): string {
  const template = dictionary[key] ?? key;
  return args.length === 0
    ? template
    : template.replace(/\{(\d+)\}/g, (match, index: string) => {
        const value = args[Number(index)];
        return value === undefined ? match : String(value);
      });
}

export function has(key: string): boolean {
  return Object.prototype.hasOwnProperty.call(dictionary, key);
}

const numberFormat = new Intl.NumberFormat(undefined, { maximumFractionDigits: 1 });
const integerFormat = new Intl.NumberFormat(undefined, { maximumFractionDigits: 0 });

export function formatNumber(value: number, integer = false): string {
  return (integer ? integerFormat : numberFormat).format(value);
}

/** Unit suffix for a metric value ("min", "km", …), already localized; empty for counts. */
export function metricUnit(metric: string, options: VizOptions): string {
  switch (metric) {
    case 'responseMinutes':
    case 'turnoutMinutes':
    case 'durationMinutes':
      return t('UnitMinutes');
    case 'stationDistance':
      return options.DistanceUnit === 'mi' ? t('UnitMiles') : t('UnitKilometers');
    default:
      return '';
  }
}

export function formatMetric(value: number | null, metric: string, options: VizOptions): string {
  if (value === null || !Number.isFinite(value)) {
    return t('NoValue');
  }

  const unit = metricUnit(metric, options);
  const integer = metric === 'count' || metric === 'unitsDispatched' || metric === 'personnelResponded' || metric === 'callsAtLocation';
  return unit ? `${formatNumber(value, integer)} ${unit}` : formatNumber(value, integer);
}

/**
 * Legend label for a category. Department data (call types, units, people, groups, custom statuses) is shown as sent;
 * buckets the server names by key (days, months, bands, outcomes, built-in statuses) are localized here.
 */
export function categoryLabel(category: Pick<VizCategory, 'Key' | 'Label'>, colorBy: VizColorBy | string, options: VizOptions): string {
  const key = category.Key;

  if (key === 'other') {
    return t('CategoryOther');
  }

  const [prefix, value] = [key.substring(0, key.indexOf(':')), key.substring(key.indexOf(':') + 1)];

  if (value === 'none') {
    switch (prefix) {
      case 'ct':
      case 'ut':
        return t('CategoryNoType');
      case 'g':
      case 's':
        return t('CategoryNoStation');
      default:
        return t('NoValue');
    }
  }

  const localized = (resourceKey: string) => (has(resourceKey) ? t(resourceKey) : category.Label);

  switch (prefix) {
    case 'tod':
      return localized(`TimeOfDay_${value}`);
    case 'dow':
      return localized(`Day_${value}`);
    case 'wp':
      return localized(value === 'weekend' ? 'Weekend' : 'Weekday');
    case 'm':
      return localized(`Month_${value}`);
    case 'o':
      return localized(`Outcome_${value}`);
    case 'stu':
      return localized(`UnitStatus_${value}`);
    case 'stp':
      return localized(`PersonnelStatus_${value}`);
    case 'rb':
      return `${category.Label} ${t('UnitMinutes')}`;
    case 'db':
      return `${category.Label} ${options.DistanceUnit === 'mi' ? t('UnitMiles') : t('UnitKilometers')}`;
    case 'rp':
      return t('RepeatBand', category.Label);
    default:
      return category.Label || (colorBy === 'none' ? '' : key);
  }
}
