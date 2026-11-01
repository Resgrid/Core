import { BarList, EmptyState, HeroNumber, KpiGrid, Note, Sparkline, SubHeading } from '../../primitives';
import type { WidgetProps } from '../types';
import { calendarDay, toneIf, utcTime } from './readinessShared';

/** Core/Resgrid.Model/Dashboard/Widgets/Readiness/ChecklistsComplianceWidgetData.cs */
export interface ChecklistsComplianceTarget {
  TargetType: number;
  TargetId?: string | null;
  Name?: string | null;
  Expected: number;
  Completed: number;
  Missed: number;
  CompletionRate?: number | null;
}

export interface ChecklistsComplianceDay {
  Day: string;
  Expected: number;
  Missed: number;
}

export interface ChecklistsComplianceWidgetData {
  FromUtc: string;
  UntilUtc: string;
  Expected: number;
  Completed: number;
  OnTime: number;
  Missed: number;
  Skipped: number;
  CompletionRate?: number | null;
  PartiallyRestricted: boolean;
  Targets: ChecklistsComplianceTarget[];
  Trend: ChecklistsComplianceDay[];
}

const COMPLIANCE_URL = '/User/Checklists/Compliance';

/** checklists.compliance (plan §7.7): completion over the last N days, the furthest-behind targets and missed checks per day. */
export default function ChecklistsComplianceWidget({ data, size, t, ctx }: WidgetProps<ChecklistsComplianceWidgetData>) {
  const k = 'W.checklists.compliance.';
  const from = utcTime(data.FromUtc);
  const until = utcTime(data.UntilUtc);
  const days = from !== null && until !== null ? Math.max(1, Math.round((until - from) / 86400000) - 1) : 30;

  if (!data.Expected) {
    return <EmptyState icon="fa-check-square-o" text={t(`${k}Empty`, days)} linkText={t(`${k}OpenCompliance`)} href={COMPLIANCE_URL} />;
  }

  const trend = data.Trend ?? [];
  const totalMissed = trend.reduce((sum, day) => sum + day.Missed, 0);
  const large = size === 'l';

  return (
    <>
      <HeroNumber value={ctx.format.percent(data.CompletionRate)} label={t(`${k}CompletedLastDays`, days)} size="md" />
      {large ? (
        <KpiGrid
          columns={4}
          items={[
            { value: ctx.format.number(data.Completed, 0), label: t(`${k}Completed`) },
            { value: ctx.format.number(data.OnTime, 0), label: t(`${k}OnTime`) },
            { value: ctx.format.number(data.Missed, 0), label: t(`${k}Missed`), tone: toneIf(data.Missed, 'bad') },
            { value: ctx.format.number(data.Skipped, 0), label: t(`${k}Skipped`) },
          ]}
        />
      ) : null}
      {data.Targets?.length ? (
        <>
          <SubHeading>{t(`${k}FurthestBehind`)}</SubHeading>
          <BarList
            max={100}
            items={data.Targets.slice(0, large ? undefined : 4).map((target) => ({
              label: target.Name || t(`${k}UnknownTarget`),
              value: target.CompletionRate ?? 0,
              display: t(`${k}RateOf`, ctx.format.percent(target.CompletionRate), target.Completed, target.Expected),
              tone: toneIf(target.Missed, 'warn'),
            }))}
          />
        </>
      ) : null}
      {!large ? <Note>{t(`${k}MissedTotal`, ctx.format.number(data.Missed, 0))}</Note> : null}
      {large && trend.length > 1 ? (
        <>
          <SubHeading>{t(`${k}MissedPerDay`)}</SubHeading>
          <Sparkline
            values={trend.map((day) => day.Missed)}
            labels={trend.map((day) => calendarDay(day.Day))}
            ariaLabel={t(`${k}TrendAria`, totalMissed, trend.length)}
            color="var(--rg-danger)"
            formatValue={(value) => t(`${k}MissedValue`, value)}
          />
        </>
      ) : null}
      {data.PartiallyRestricted ? <Note>{t(`${k}Restricted`)}</Note> : null}
    </>
  );
}
