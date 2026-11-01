import { HeroNumber, KpiGrid, Sparkline, type KpiItem } from '../../primitives';
import type { WidgetProps } from '../types';
import './opsWidgets.css';

/** Core/Resgrid.Model/Dashboard/Widgets/Operations/CallsKpisWidgetData.cs */
export interface CallsKpisDay {
  DayUtc: string;
  Count: number;
}

export interface CallsKpisWidgetData {
  ActiveCount: number;
  TodayCount: number;
  PendingCount: number;
  ScheduledCount: number;
  AwaitingResourcesCount?: number | null;
  Last7Days: CallsKpisDay[];
  ActiveUrl?: string | null;
  PendingUrl?: string | null;
  ScheduledUrl?: string | null;
  AwaitingResourcesUrl?: string | null;
}

/** calls.kpis (plan §7.1): active calls, today's, pending, scheduled and awaiting-resources counts, and the last 7 days. */
export default function CallsKpisWidget({ data, size, t, ctx }: WidgetProps<CallsKpisWidgetData>) {
  const k = 'W.calls.kpis.';
  const days = data.Last7Days ?? [];
  const hasAwaiting = data.AwaitingResourcesCount !== null && data.AwaitingResourcesCount !== undefined;

  const hero = (
    <a className="rg-ops-herolink" href={data.ActiveUrl ?? '/User/Dispatch/Dashboard'}>
      <HeroNumber value={ctx.format.number(data.ActiveCount, 0)} label={t(`${k}ActiveCalls`)} size={size === 'xs' ? 'md' : 'lg'} />
    </a>
  );

  if (size === 'xs') {
    return (
      <>
        {hero}
        <div className="rg-m">{t(`${k}TodayLine`, ctx.format.number(data.TodayCount, 0))}</div>
      </>
    );
  }

  const tiles: KpiItem[] = [
    { value: ctx.format.number(data.TodayCount, 0), label: t(`${k}Today`), href: data.ActiveUrl ?? undefined },
    { value: ctx.format.number(data.PendingCount, 0), label: t(`${k}Pending`), href: data.PendingUrl ?? undefined, tone: data.PendingCount > 0 ? 'info' : undefined },
    { value: ctx.format.number(data.ScheduledCount, 0), label: t(`${k}Scheduled`), href: data.ScheduledUrl ?? undefined },
  ];
  if (hasAwaiting) {
    tiles.push({
      value: ctx.format.number(data.AwaitingResourcesCount, 0),
      label: t(`${k}Awaiting`),
      href: data.AwaitingResourcesUrl ?? undefined,
      tone: (data.AwaitingResourcesCount ?? 0) > 0 ? 'warn' : undefined,
    });
  }

  const labels = days.map((d) => ctx.format.date(d.DayUtc));
  const values = days.map((d) => d.Count);

  return (
    <>
      {hero}
      {values.length > 1 ? (
        <Sparkline
          values={values}
          labels={labels}
          ariaLabel={t(`${k}SparkAria`, values.join(', '))}
          formatValue={(value) => t(`${k}CallsCount`, ctx.format.number(value, 0))}
        />
      ) : null}
      {size === 'm' ? (
        <div className="rg-ops-gap">
          <KpiGrid items={tiles} columns={tiles.length} />
        </div>
      ) : (
        // Two rows of label/count pairs: an s card is only two grid rows tall.
        <ul className="rg-legend rg-ops-legend2">
          {tiles.map((tile) => (
            <li key={tile.label}>
              {tile.tone === 'warn' ? <i className="fa fa-exclamation-triangle rg-tone-warn rg-list-icon" aria-hidden="true" /> : null}
              {tile.href ? <a href={tile.href}>{tile.label}</a> : <span>{tile.label}</span>}
              <b>{tile.value}</b>
            </li>
          ))}
        </ul>
      )}
    </>
  );
}
