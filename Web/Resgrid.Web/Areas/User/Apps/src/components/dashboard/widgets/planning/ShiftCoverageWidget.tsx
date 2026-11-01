import { DenseTable, EmptyState, Note, StatusChip, type TableColumn, type TableGroup } from '../../primitives';
import { useWidgetBadge } from '../hooks';
import type { WidgetProps } from '../types';
import { calendarDayLabel, timeRange } from './planningTime';

interface CoverageItem {
  Key: string;
  Kind: 'shift' | 'workshift' | string;
  ShiftId?: number | null;
  ShiftDayId?: number | null;
  WorkshiftDayId?: string | null;
  Name?: string | null;
  Color?: string | null;
  /** Department-local "YYYY-MM-DD". */
  Day: string;
  StartUtc: string;
  EndUtc: string;
  Source: 'manual' | 'automatic' | string;
  People: number;
  AutomaticPeople: number;
  AddedCount: number;
  RemovedCount: number;
  IsGap: boolean;
  IsActive: boolean;
  IsOver: boolean;
}

/** Core/Resgrid.Model/Dashboard/Widgets/Planning/ShiftCoverageWidgetData.cs */
export interface ShiftCoverageWidgetData {
  CanView: boolean;
  Days: number;
  StartDay?: string | null;
  GapCount: number;
  Items: CoverageItem[];
  StaffingUrl: string;
}

/** shifts.coverage (plan §7.6): the coming days' staffing by day, approved or automatic, unstaffed shifts flagged. */
export default function ShiftCoverageWidget({ data, size, t, ctx }: WidgetProps<ShiftCoverageWidgetData>) {
  useWidgetBadge(ctx, data.CanView && data.GapCount > 0 ? data.GapCount : null);

  if (!data.CanView) {
    return <EmptyState icon="fa-users" text={t('W.shifts.coverage.NotSupervisor')} />;
  }
  if (data.Items.length === 0) {
    return <EmptyState icon="fa-calendar-o" text={t('W.shifts.coverage.Empty')} linkText={t('W.shifts.coverage.OpenStaffing')} href={data.StaffingUrl} />;
  }

  const columns: TableColumn<CoverageItem>[] = [
    {
      key: 'name',
      header: t('W.shifts.coverage.Shift'),
      maxWidth: 220,
      render: (row) => (
        <a href={data.StaffingUrl}>
          <StatusChip label={row.Name || (row.Kind === 'workshift' ? t('W.shifts.coverage.StaticShift') : t('W.shifts.coverage.Shift'))} color={row.Color} />
        </a>
      ),
    },
    { key: 'time', header: t('W.shifts.coverage.Time'), muted: true, render: (row) => timeRange(ctx.format, row.StartUtc, row.EndUtc) },
    {
      key: 'people',
      header: t('W.shifts.coverage.Staffed'),
      align: 'end',
      render: (row) =>
        row.IsGap ? (
          <span style={{ color: 'var(--rg-danger-text)' }}>
            <i className="fa fa-exclamation-circle" aria-hidden="true" /> {t('W.shifts.coverage.Unstaffed')}
          </span>
        ) : (
          <span className="rg-num">{t('W.shifts.coverage.People', ctx.format.number(row.People, 0))}</span>
        ),
    },
    {
      key: 'source',
      header: t('W.shifts.coverage.Source'),
      muted: true,
      render: (row) =>
        row.Source === 'manual'
          ? `${t('W.shifts.coverage.Manual')}${row.AddedCount + row.RemovedCount > 0 ? ` (${t('W.shifts.coverage.Changes', row.AddedCount, row.RemovedCount)})` : ''}`
          : t('W.shifts.coverage.Automatic'),
    },
  ];

  const days = new Map<string, CoverageItem[]>();
  for (const item of data.Items) {
    const list = days.get(item.Day) ?? [];
    list.push(item);
    days.set(item.Day, list);
  }

  const groups: TableGroup<CoverageItem>[] = Array.from(days.entries()).map(([day, rows]) => {
    const gaps = rows.filter((row) => row.IsGap && !row.IsOver).length;
    return {
      key: day,
      label: calendarDayLabel(day, t, ctx.format.timeZone, 'W.shifts.coverage.Today', 'W.shifts.coverage.Tomorrow'),
      meta: gaps > 0 ? t('W.shifts.coverage.Gaps', gaps) : undefined,
      rows,
    };
  });

  return (
    <>
      <DenseTable columns={size === 'xl' ? columns : columns.filter((column) => column.key !== 'source')} groups={groups} rowKey={(row) => row.Key} />
      {data.GapCount > 0 ? <Note style={{ marginTop: 6 }}>{t('W.shifts.coverage.Gaps', data.GapCount)}</Note> : null}
    </>
  );
}
