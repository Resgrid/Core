import { EmptyState, ItemList } from '../../primitives';
import { useWidgetBadge } from '../hooks';
import type { WidgetProps } from '../types';
import { enc, ReadOnlyNote, SELECTOR_URL } from './iboShared';
import './ibo.css';

/** Core/Resgrid.Model/Dashboard/Widgets/IncidentBackOffice/IncidentActivationsWidgetData.cs */
export interface IncidentActivationsWidgetData {
  ReadOnly: boolean;
  OpenCount: number;
  MineCount: number;
  Rows: IncidentActivationRow[];
}

export interface IncidentActivationRow {
  ActivationId: string;
  Name?: string | null;
  ShortCode?: string | null;
  Status: string;
  Kind: string;
  IsExercise: boolean;
  OpenedOn?: string | null;
  PeriodNumber?: number | null;
  PeriodStartOn?: string | null;
  PeriodEndOn?: string | null;
  MyPositionCode?: string | null;
  MyPositionName?: string | null;
}

/** ibo.activations (plan §7.10): open incidents with their current operational period and the position you hold. */
export default function IncidentActivationsWidget({ data, t, ctx }: WidgetProps<IncidentActivationsWidgetData>) {
  const k = 'W.ibo.activations.';
  useWidgetBadge(ctx, data.OpenCount || null);

  const rows = data.Rows ?? [];
  if (rows.length === 0) {
    return (
      <>
        <EmptyState icon="fa-sitemap" text={t(`${k}Empty`)} linkText={t('W.ibo.OpenIncidents')} href={SELECTOR_URL} />
        <ReadOnlyNote readOnly={data.ReadOnly} t={t} />
      </>
    );
  }

  return (
    <>
      <ItemList
        items={rows.map((row) => {
          const period =
            row.PeriodNumber !== null && row.PeriodNumber !== undefined
              ? `${t(`${k}Period`, row.PeriodNumber)} · ${ctx.format.time(row.PeriodStartOn)}–${ctx.format.time(row.PeriodEndOn)}`
              : row.OpenedOn
                ? t(`${k}OpenedOn`, ctx.format.date(row.OpenedOn))
                : null;
          const position = row.MyPositionCode ? `${t(`${k}MyPosition`)}: ${row.MyPositionName || row.MyPositionCode}` : null;
          return {
            key: row.ActivationId,
            title: (
              <>
                {row.Name || row.ShortCode}
                {row.ShortCode && row.Name ? <span className="rg-mu"> ({row.ShortCode})</span> : null}
              </>
            ),
            meta: [t(`W.ibo.Kind.${row.Kind}`), period, position, row.IsExercise ? t('W.ibo.Exercise') : null].filter(Boolean).join(' · '),
            icon: row.MyPositionCode ? 'fa-id-badge' : 'fa-sitemap',
            right: <span className="rg-tag">{t(`W.ibo.Status.${row.Status}`)}</span>,
            href: `/User/IncidentBackOffice/Select?aid=${enc(row.ActivationId)}`,
          };
        })}
      />
      <ReadOnlyNote readOnly={data.ReadOnly} t={t} />
    </>
  );
}
