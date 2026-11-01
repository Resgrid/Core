import { BarList, EmptyState, KpiGrid, Note, SubHeading } from '../../primitives';
import type { WidgetProps } from '../types';
import { isCompact, toneIf, utcTime } from './readinessShared';

/** Core/Resgrid.Model/Dashboard/Widgets/Readiness/WorkOrdersStatsWidgetData.cs */
export interface WorkOrdersStatsWidgetData {
  FromUtc: string;
  UntilUtc: string;
  Total: number;
  Open: number;
  Overdue: number;
  PreventiveDue: number;
  PreventiveOnTime: number;
  PreventiveCompliancePercent?: number | null;
  MeanTimeToRepairHours?: number | null;
  DowntimeHours: number;
  ResponseSlaBreaches: number;
  RepairSlaBreaches: number;
  /** Low, Normal, High, Emergency. */
  OpenByPriority: number[];
  /** 0–6, 7–29, 30–89, 90+ days. */
  OpenByAge: number[];
}

const REPORTS_URL = '/User/WorkOrders/Reports';
const LIST_URL = '/User/WorkOrders';

/** workOrders.stats (plan §7.7): open, overdue, preventive maintenance and repair time from the Reports page. */
export default function WorkOrdersStatsWidget({ data, size, t, ctx }: WidgetProps<WorkOrdersStatsWidgetData>) {
  const k = 'W.workOrders.stats.';
  if (!data.Total && !data.Open) {
    return <EmptyState icon="fa-wrench" text={t(`${k}Empty`)} linkText={t(`${k}OpenWorkOrders`)} href={LIST_URL} />;
  }

  const hours = (value: number | null | undefined) => (value === null || value === undefined ? '—' : ctx.format.duration(value * 60));
  const compact = isCompact(size);
  const priorities = data.OpenByPriority ?? [];
  const breaches = (data.ResponseSlaBreaches ?? 0) + (data.RepairSlaBreaches ?? 0);
  const from = utcTime(data.FromUtc);
  const until = utcTime(data.UntilUtc);
  const days = from !== null && until !== null ? Math.max(1, Math.round((until - from) / 86400000)) : null;

  return (
    <>
      <KpiGrid
        columns={2}
        items={[
          { value: ctx.format.number(data.Open, 0), label: t(`${k}Open`), href: LIST_URL },
          { value: ctx.format.number(data.Overdue, 0), label: t(`${k}Overdue`), tone: toneIf(data.Overdue, 'bad'), href: LIST_URL },
          {
            value: data.PreventiveDue ? ctx.format.percent(data.PreventiveCompliancePercent) : '—',
            label: t(`${k}PreventiveOnTime`),
            title: t(`${k}PreventiveOfDue`, data.PreventiveOnTime, data.PreventiveDue),
          },
          { value: hours(data.MeanTimeToRepairHours), label: t(`${k}Mttr`) },
        ]}
      />
      {!compact ? (
        <>
          <SubHeading>{t(`${k}OpenByPriority`)}</SubHeading>
          <BarList
            items={[3, 2, 1, 0].map((priority) => ({
              label: t(`${k}Priority${priority}`),
              value: priorities[priority] ?? 0,
              tone: priority === 3 ? toneIf(priorities[priority], 'bad') : priority === 2 ? toneIf(priorities[priority], 'warn') : undefined,
            }))}
          />
          <Note>
            {t(`${k}Downtime`, hours(data.DowntimeHours))}
            {breaches > 0 ? ` · ${t(`${k}SlaBreaches`, breaches)}` : ''}
            {' · '}
            <a href={REPORTS_URL}>{days ? t(`${k}LastDays`, days) : t(`${k}OpenReports`)}</a>
          </Note>
        </>
      ) : null}
    </>
  );
}
