import { EmptyState, ItemList } from '../../primitives';
import type { WidgetProps } from '../types';
import { timeRange } from './planningTime';

/** Core/Resgrid.Model/Dashboard/Widgets/Planning/ShiftsTodayWidgetData.cs */
export interface ShiftsTodayWidgetData {
  /** Department-local "YYYY-MM-DD". */
  Day: string;
  Shifts: Array<{
    ShiftId: number;
    ShiftDayId: number;
    Name?: string | null;
    Color?: string | null;
    StartUtc: string;
    EndUtc: string;
    IsActive: boolean;
    IsOver: boolean;
    IsFilled: boolean;
    OpenSlots: number;
    OnDutyCount: number;
    Groups: Array<{ GroupId?: number | null; GroupName?: string | null; OnDutyCount: number }>;
    Url: string;
  }>;
}

/** shifts.today (plan §7.6): today's shift days, staffing against the requirement and who is on duty by group. */
export default function ShiftsTodayWidget({ data, size, t, ctx }: WidgetProps<ShiftsTodayWidgetData>) {
  if (data.Shifts.length === 0) {
    return <EmptyState icon="fa-briefcase" text={t('W.shifts.today.Empty')} linkText={t('W.shifts.today.OpenShifts')} href="/User/Shifts" />;
  }

  return (
    <ItemList
      items={data.Shifts.map((shift) => {
        const groups = shift.Groups.map((group) => t('W.shifts.today.GroupCount', group.GroupName || t('W.shifts.today.NoGroup'), ctx.format.number(group.OnDutyCount, 0)));
        const state = shift.IsOver ? t('W.shifts.today.Ended') : shift.IsActive ? t('W.shifts.today.Active') : null;
        return {
          key: String(shift.ShiftDayId),
          title: shift.Name || t('W.shifts.today.Shift'),
          href: shift.Url,
          accentColor: shift.Color || undefined,
          meta: [
            timeRange(ctx.format, shift.StartUtc, shift.EndUtc),
            state,
            t('W.shifts.today.OnDuty', ctx.format.number(shift.OnDutyCount, 0)),
            size === 'l' && groups.length > 0 ? groups.join(', ') : null,
          ]
            .filter(Boolean)
            .join(' · '),
          right: shift.IsFilled ? (
            <span className="rg-m">
              <i className="fa fa-check-circle" aria-hidden="true" style={{ color: 'var(--rg-good-text)' }} /> {t('W.shifts.today.Filled')}
            </span>
          ) : (
            <span className="rg-m" style={{ color: 'var(--rg-warning-text)' }}>
              <i className="fa fa-exclamation-triangle" aria-hidden="true" /> {t('W.shifts.today.OpenSlots', ctx.format.number(shift.OpenSlots, 0))}
            </span>
          ),
        };
      })}
    />
  );
}
