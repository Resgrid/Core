import { KpiGrid, Note, Sparkline, SubHeading, type KpiItem } from '../../primitives';
import type { WidgetProps } from '../types';
import { toneIf } from '../readiness/readinessShared';

/** Core/Resgrid.Model/Dashboard/Widgets/Records/RecordsAnalyticsWidgetData.cs */
export interface RecordsAnalyticsKpi {
  Key: string;
  Current: number;
  Prior: number;
  ChangePercent?: number | null;
}

export interface RecordsAnalyticsPoint {
  Key: string;
  Label?: string | null;
  Count: number;
}

export interface RecordsAnalyticsWidgetData {
  StartUtc: string;
  EndUtc: string;
  GroupScoped: boolean;
  Truncated: boolean;
  OverdueNow: number;
  WentOverdue: number;
  OpenDrafts: number;
  AwaitingReview: number;
  MedianHoursToFinalize?: number | null;
  MedianReviewHours?: number | null;
  ReturnRatePercent?: number | null;
  Kpis: RecordsAnalyticsKpi[];
  ByMonth: RecordsAnalyticsPoint[];
}

const ANALYTICS_URL = '/User/RecordsAnalytics';

/** records.analytics (plan §7.8): overdue now, time to finalize and review, return rate and records per month. */
export default function RecordsAnalyticsWidget({ data, size, t, ctx }: WidgetProps<RecordsAnalyticsWidgetData>) {
  const k = 'W.records.analytics.';
  const hours = (value: number | null | undefined) => (value === null || value === undefined ? '—' : ctx.format.duration(value * 60));
  const items: KpiItem[] = [
    { value: ctx.format.number(data.OverdueNow, 0), label: t(`${k}OverdueNow`), tone: toneIf(data.OverdueNow, 'bad'), href: ANALYTICS_URL },
    { value: hours(data.MedianHoursToFinalize), label: t(`${k}MedianToFinalize`), href: ANALYTICS_URL },
    { value: hours(data.MedianReviewHours), label: t(`${k}MedianReview`), href: ANALYTICS_URL },
    { value: ctx.format.percent(data.ReturnRatePercent, 1), label: t(`${k}ReturnRate`), href: ANALYTICS_URL },
  ];
  if (size === 'l') {
    items.push(
      { value: ctx.format.number(data.OpenDrafts, 0), label: t(`${k}OpenDrafts`), href: ANALYTICS_URL },
      { value: ctx.format.number(data.AwaitingReview, 0), label: t(`${k}AwaitingReview`), href: ANALYTICS_URL },
      { value: ctx.format.number(data.WentOverdue, 0), label: t(`${k}WentOverdue`), tone: toneIf(data.WentOverdue, 'warn'), href: ANALYTICS_URL },
    );
  }

  const months = data.ByMonth ?? [];
  const total = months.reduce((sum, month) => sum + month.Count, 0);

  return (
    <>
      <KpiGrid columns={size === 'l' ? 4 : 2} items={items} />
      {months.length > 1 ? (
        <>
          <SubHeading>{t(`${k}RecordsPerMonth`)}</SubHeading>
          <Sparkline
            values={months.map((month) => month.Count)}
            labels={months.map((month) => month.Label || month.Key)}
            ariaLabel={t(`${k}TrendAria`, total, months.length)}
            formatValue={(value) => ctx.format.number(value, 0)}
          />
        </>
      ) : null}
      {data.GroupScoped ? <Note>{t(`${k}GroupScoped`)}</Note> : null}
      {data.Truncated ? <Note>{t(`${k}Truncated`)}</Note> : null}
    </>
  );
}
