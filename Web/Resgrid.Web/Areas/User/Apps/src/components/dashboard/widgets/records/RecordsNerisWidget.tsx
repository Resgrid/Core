import { EmptyState, KpiGrid, type KpiItem } from '../../primitives';
import type { WidgetProps } from '../types';
import { isCompact, toneIf } from '../readiness/readinessShared';

/** Core/Resgrid.Model/Dashboard/Widgets/Records/RecordsNerisWidgetData.cs */
export interface RecordsNerisWidgetData {
  Incomplete: number;
  AwaitingReview: number;
  Submitted: number;
  Accepted: number;
  Rejected: number;
}

const DASHBOARD_URL = '/User/Records/Dashboard';

/** records.neris (plan §7.8): incident reports by filing stage. */
export default function RecordsNerisWidget({ data, size, t, ctx }: WidgetProps<RecordsNerisWidgetData>) {
  const k = 'W.records.neris.';
  if (!data.Incomplete && !data.AwaitingReview && !data.Submitted && !data.Accepted && !data.Rejected) {
    return <EmptyState icon="fa-paper-plane" text={t(`${k}Empty`)} linkText={t(`${k}OpenRecords`)} href={DASHBOARD_URL} />;
  }

  const n = (value: number) => ctx.format.number(value, 0);
  const rejected: KpiItem = { value: n(data.Rejected), label: t(`${k}Rejected`), tone: toneIf(data.Rejected, 'bad'), href: DASHBOARD_URL };
  const items: KpiItem[] = isCompact(size)
    ? [{ value: n(data.Incomplete), label: t(`${k}Incomplete`), href: DASHBOARD_URL }, rejected]
    : [
        { value: n(data.Incomplete), label: t(`${k}Incomplete`), href: DASHBOARD_URL },
        { value: n(data.AwaitingReview), label: t(`${k}AwaitingReview`), href: DASHBOARD_URL },
        { value: n(data.Submitted), label: t(`${k}Submitted`), href: DASHBOARD_URL },
        { value: n(data.Accepted), label: t(`${k}Accepted`), tone: toneIf(data.Accepted, 'good'), href: DASHBOARD_URL },
        rejected,
      ];

  return <KpiGrid columns={isCompact(size) ? 2 : 3} items={items} />;
}
