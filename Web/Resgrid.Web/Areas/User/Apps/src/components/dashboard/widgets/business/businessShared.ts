import type { Tone } from '../../primitives';
import type { SizeToken, WidgetFormat } from '../types';

// Helpers shared by the Workforce & Business widgets (agent D slice).

/** Core/Resgrid.Model/Dashboard/Widgets/Business/BusinessWidgetCommon.cs — an amount in one currency. */
export interface CurrencyAmount {
  Currency?: string | null;
  Amount: number;
}

/** xs / s show the headline only. */
export function isCompact(size: SizeToken): boolean {
  return size === 'xs' || size === 's';
}

/** The rows a size has room for (the server already applied maxRows). */
export function rowsFor<T>(items: T[] | null | undefined, size: SizeToken, compactRows = 2): T[] {
  const list = items ?? [];
  return isCompact(size) ? list.slice(0, compactRows) : list;
}

/** The tone only when there is something to flag: never color a zero. */
export function toneIf(value: number | null | undefined, tone: Tone): Tone | undefined {
  return value ? tone : undefined;
}

/** Per-currency balances, never summed across currencies: "$1,200.00 · CA$50.00". */
export function amounts(list: CurrencyAmount[] | null | undefined, format: WidgetFormat, fallbackCurrency = 'USD'): string {
  const items = (list ?? []).filter((item) => item && Number.isFinite(item.Amount));
  if (items.length === 0) {
    return format.currency(0, fallbackCurrency);
  }
  return items.map((item) => format.currency(item.Amount, item.Currency || fallbackCurrency)).join(' · ');
}

export function enc(value: string | number | null | undefined): string {
  return encodeURIComponent(value === null || value === undefined ? '' : String(value));
}
