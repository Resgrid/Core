import { uiLocale } from '../../i18n';
import { EmptyState, Note } from '../../primitives';
import type { SizeToken, Translate } from '../types';

// Helpers shared by the Incident Back Office widgets (agent D slice).

/** Core/Resgrid.Model/Dashboard/Widgets/IncidentBackOffice/IncidentWidgetDataBase.cs — every activation-scoped widget's header. */
export interface IncidentWidgetDataBase {
  ReadOnly: boolean;
  HasActivation: boolean;
  ActivationId?: string | null;
  ActivationName?: string | null;
  ShortCode?: string | null;
  /** IncidentActivationStatuses name. */
  ActivationStatus?: string | null;
  IsExercise: boolean;
  Currency?: string | null;
}

export interface IncidentKeyCount {
  Key: string;
  Count: number;
}

export interface IncidentArrivalWindow {
  Window: string;
  Until?: string | null;
  Count: number;
  Apparatus: number;
  Personnel: number;
  Predicted: number;
}

export const SELECTOR_URL = '/User/IncidentBackOffice';

export function isCompact(size: SizeToken): boolean {
  return size === 'xs' || size === 's';
}

/** Incident-scale money in whole units ("$1,180,000"): cents add width, not information, at budget level. */
export function wholeCurrency(value: number | null | undefined, currency: string): string {
  if (value === null || value === undefined || !Number.isFinite(value)) {
    return '—';
  }
  try {
    return new Intl.NumberFormat(uiLocale(), { style: 'currency', currency, maximumFractionDigits: 0 }).format(value);
  } catch {
    return new Intl.NumberFormat(uiLocale(), { maximumFractionDigits: 0 }).format(value);
  }
}

/** Money for a KPI tile ("$1.3M"): a 92 px tile on a phone has no room for "$1,180,000"; the tile's title carries the whole amount. */
export function compactCurrency(value: number | null | undefined, currency: string): string {
  if (value === null || value === undefined || !Number.isFinite(value)) {
    return '—';
  }
  try {
    return new Intl.NumberFormat(uiLocale(), { style: 'currency', currency, notation: 'compact', maximumFractionDigits: 1 }).format(value);
  } catch {
    return wholeCurrency(value, currency);
  }
}

export function enc(value: string | number | null | undefined): string {
  return encodeURIComponent(value === null || value === undefined ? '' : String(value));
}

/** A desk page for the widget's activation (the desk base reads `aid` from the query string). */
export function deskUrl(path: string, activationId: string | null | undefined): string {
  return activationId ? `${path}?aid=${enc(activationId)}` : SELECTOR_URL;
}

/** The incident the widget shows: name (CODE), status and the exercise tag, linking to its desk. */
export function ActivationHeader({ data, t, href }: { data: IncidentWidgetDataBase; t: Translate; href: string }) {
  if (!data.HasActivation) {
    return null;
  }
  return (
    <div className="rg-ibo-head">
      <a href={href} className="rg-ibo-name">
        {data.ActivationName || data.ShortCode}
        {data.ShortCode && data.ActivationName ? <span className="rg-mu"> ({data.ShortCode})</span> : null}
      </a>
      {data.ActivationStatus ? <span className="rg-tag">{t(`W.ibo.Status.${data.ActivationStatus}`)}</span> : null}
      {data.IsExercise ? <span className="rg-tag">{t('W.ibo.Exercise')}</span> : null}
    </div>
  );
}

/** "No open incident" — the chosen incident is gone or none is open. */
export function NoActivation({ t }: { t: Translate }) {
  return <EmptyState icon="fa-sitemap" text={t('W.ibo.NoActivation')} linkText={t('W.ibo.OpenIncidents')} href={SELECTOR_URL} />;
}

/** The lapsed add-on note (the module's banner, small and muted here). */
export function ReadOnlyNote({ readOnly, t }: { readOnly: boolean; t: Translate }) {
  return readOnly ? (
    <Note>
      <i className="fa fa-lock" aria-hidden="true" /> {t('W.ibo.ReadOnly')}
    </Note>
  ) : null;
}
