import { BarList, EmptyState, KpiGrid, Note, Sparkline, SubHeading } from '../../primitives';
import type { WidgetProps } from '../types';
import type { CallPriorityData } from './opsShared';
import './opsWidgets.css';

/** Core/Resgrid.Model/Dashboard/Widgets/Operations/CallsVolumeWidgetData.cs */
export interface CallsVolumeDay {
  DayUtc: string;
  Count: number;
}

export interface CallsVolumeSlice {
  Label?: string | null;
  Count: number;
  IsOther: boolean;
}

export interface CallsVolumePriority {
  Priority: CallPriorityData;
  Count: number;
}

export interface CallsVolumeWidgetData {
  Days: number;
  StartUtc: string;
  EndUtc: string;
  TotalCalls: number;
  Daily: CallsVolumeDay[];
  ByType: CallsVolumeSlice[];
  ByPriority: CallsVolumePriority[];
  MedianResponseSeconds?: number | null;
  P90ResponseSeconds?: number | null;
  ResponseSampleCount: number;
  ReportsUrl?: string | null;
}

/** calls.volume (plan §7.1): calls per day, by type and priority, and the response-time median, from the Reports module. */
export default function CallsVolumeWidget({ data, size, t, ctx }: WidgetProps<CallsVolumeWidgetData>) {
  const k = 'W.calls.volume.';
  const reportsUrl = data.ReportsUrl ?? '/User/Reports/Index';

  if (!data.TotalCalls) {
    return <EmptyState icon="fa-bar-chart" text={t(`${k}Empty`, data.Days)} linkText={t(`${k}OpenReports`)} href={reportsUrl} />;
  }

  const days = data.Daily ?? [];
  const minutes = (seconds: number | null | undefined) => (seconds === null || seconds === undefined ? null : Math.round(seconds / 60));
  const average = days.length ? data.TotalCalls / days.length : 0;
  // The card is three rows tall at l and xl: five bars per list, the rest folded into "Other".
  const MAX_BARS = 5;
  const typeSlices = data.ByType ?? [];
  const typeRows =
    typeSlices.length <= MAX_BARS
      ? typeSlices
      : [
          ...typeSlices.filter((s) => !s.IsOther).slice(0, MAX_BARS - 1),
          {
            Label: null,
            IsOther: true,
            Count: typeSlices.filter((s) => !s.IsOther).slice(MAX_BARS - 1).reduce((sum, s) => sum + s.Count, 0) + typeSlices.filter((s) => s.IsOther).reduce((sum, s) => sum + s.Count, 0),
          },
        ];

  return (
    <>
      <KpiGrid
        columns={3}
        items={[
          { value: ctx.format.number(data.TotalCalls, 0), label: t(`${k}TotalCalls`, data.Days), href: reportsUrl },
          { value: ctx.format.number(average, 1), label: t(`${k}PerDay`) },
          {
            value: data.MedianResponseSeconds !== null && data.MedianResponseSeconds !== undefined ? ctx.format.duration(minutes(data.MedianResponseSeconds)) : '—',
            label: t(`${k}MedianResponse`),
            title:
              data.P90ResponseSeconds !== null && data.P90ResponseSeconds !== undefined
                ? t(`${k}P90Title`, ctx.format.duration(minutes(data.P90ResponseSeconds)), ctx.format.number(data.ResponseSampleCount, 0))
                : undefined,
          },
        ]}
      />
      <SubHeading>{t(`${k}PerDayHeading`)}</SubHeading>
      <Sparkline
        values={days.map((d) => d.Count)}
        labels={days.map((d) => ctx.format.date(d.DayUtc))}
        height={size === 'xl' ? 60 : 48}
        ariaLabel={t(`${k}SparkAria`, data.Days, days.map((d) => d.Count).join(', '))}
        formatValue={(value) => t(`${k}CallsCount`, ctx.format.number(value, 0))}
      />
      <div className="rg-volume-cols">
        <div>
          <SubHeading>{t(`${k}ByType`)}</SubHeading>
          <BarList
            items={typeRows.map((slice) => ({
              label: slice.IsOther ? t(`${k}OtherTypes`) : slice.Label || t(`${k}Untyped`),
              value: slice.Count,
              display: ctx.format.number(slice.Count, 0),
            }))}
          />
        </div>
        <div>
          <SubHeading>{t(`${k}ByPriority`)}</SubHeading>
          <BarList
            items={(data.ByPriority ?? []).slice(0, MAX_BARS).map((row) => ({
              label: row.Priority?.Name || t(`${k}Untyped`),
              value: row.Count,
              display: ctx.format.number(row.Count, 0),
              color: row.Priority?.Color || undefined,
            }))}
          />
        </div>
      </div>
      {size === 'xl' && data.ResponseSampleCount > 0 ? <Note>{t(`${k}ResponseNote`, ctx.format.number(data.ResponseSampleCount, 0))}</Note> : null}
    </>
  );
}
