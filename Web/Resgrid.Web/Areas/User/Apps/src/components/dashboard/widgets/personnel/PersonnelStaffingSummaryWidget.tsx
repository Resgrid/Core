import { BarList, EmptyState, HeroNumber, StackBar, SubHeading } from '../../primitives';
import type { WidgetProps } from '../types';
import type { PersonnelSummaryItem } from './PersonnelStatusSummaryWidget';
import '../operations/opsWidgets.css';
import './personnelWidgets.css';

/** Core/Resgrid.Model/Dashboard/Widgets/Personnel/PersonnelStaffingSummaryWidgetData.cs */
export interface PersonnelStaffingGroup {
  GroupId: number;
  Name?: string | null;
  Total: number;
  AvailableCount: number;
  OnDutyCount?: number | null;
}

export interface PersonnelStaffingSummaryWidgetData {
  Total: number;
  AvailableCount: number;
  OnDutyCount?: number | null;
  ByStaffing: PersonnelSummaryItem[];
  ByGroup: PersonnelStaffingGroup[];
  PersonnelUrl?: string | null;
}

/** personnel.staffingSummary (plan §7.3): staffing levels across the department and by group, with who is on duty. */
export default function PersonnelStaffingSummaryWidget({ data, size, t, ctx }: WidgetProps<PersonnelStaffingSummaryWidgetData>) {
  const k = 'W.personnel.staffingSummary.';

  if (!data.Total) {
    return <EmptyState icon="fa-id-badge" text={t(`${k}Empty`)} />;
  }

  const onDuty = data.OnDutyCount !== null && data.OnDutyCount !== undefined;
  const hero = (
    <HeroNumber
      value={ctx.format.number(onDuty ? data.OnDutyCount : data.AvailableCount, 0)}
      label={onDuty ? t(`${k}OnDutyOf`, ctx.format.number(data.Total, 0)) : t(`${k}AvailableOf`, ctx.format.number(data.Total, 0))}
      size={size === 'xs' ? 'md' : 'lg'}
    />
  );
  if (size === 'xs') {
    return hero;
  }

  const parts = (data.ByStaffing ?? []).map((item) => ({ label: item.Status?.Text || '', value: item.Count, color: item.Status?.Color || 'var(--rg-muted)' }));
  const groups = (data.ByGroup ?? []).filter((g) => g.Total > 0);
  const max = Math.max(1, ...groups.map((g) => g.Total));

  const ariaLabel = t(`${k}StackAria`, parts.map((p) => `${p.label} ${p.value}`).join(', '));
  if (size === 'm' && groups.length > 0) {
    // An m card is as short as an s card, only wider: the groups go beside the levels.
    return (
      <div className="rg-ops-split">
        <div>
          {hero}
          <StackBar parts={parts} ariaLabel={ariaLabel} showLegend />
        </div>
        <div>
          <SubHeading>{t(`${k}ByGroup`)}</SubHeading>
          <BarList
            max={max}
            items={groups.map((group) => ({
              label: group.Name || '',
              value: group.AvailableCount,
              display:
                group.OnDutyCount !== null && group.OnDutyCount !== undefined
                  ? t(`${k}GroupOnDuty`, group.AvailableCount, group.Total, group.OnDutyCount)
                  : t(`${k}GroupAvailable`, group.AvailableCount, group.Total),
              tone: group.AvailableCount === 0 ? ('warn' as const) : undefined,
            }))}
          />
        </div>
      </div>
    );
  }

  return (
    <>
      {hero}
      <StackBar parts={parts} ariaLabel={ariaLabel} showLegend />
    </>
  );
}
