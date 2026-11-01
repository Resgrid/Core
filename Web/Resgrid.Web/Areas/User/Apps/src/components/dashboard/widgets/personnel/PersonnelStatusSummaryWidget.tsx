import { EmptyState, HeroNumber, Legend, StackBar } from '../../primitives';
import type { WidgetProps } from '../types';
import type { StatusChipData } from '../operations/opsShared';
import '../operations/opsWidgets.css';
import './personnelWidgets.css';

/** Core/Resgrid.Model/Dashboard/Widgets/Personnel/PersonnelStatusSummaryWidgetData.cs */
export interface PersonnelSummaryItem {
  Status: StatusChipData;
  Count: number;
}

export interface PersonnelStatusSummaryWidgetData {
  Total: number;
  Available: number;
  Committed: number;
  Delayed: number;
  Unavailable: number;
  Unknown: number;
  ByStatus: PersonnelSummaryItem[];
  PersonnelUrl?: string | null;
}

/** personnel.statusSummary (plan §7.3): how many people are responding, on scene, available or unavailable. */
export default function PersonnelStatusSummaryWidget({ data, size, t, ctx }: WidgetProps<PersonnelStatusSummaryWidgetData>) {
  const k = 'W.personnel.statusSummary.';
  const url = data.PersonnelUrl ?? '/User/Personnel/Index';

  if (!data.Total) {
    return <EmptyState icon="fa-user" text={t(`${k}Empty`)} />;
  }

  const hero = (
    <HeroNumber value={ctx.format.number(data.Committed, 0)} label={t(`${k}CommittedOf`, ctx.format.number(data.Total, 0))} size={size === 'xs' ? 'md' : 'lg'} />
  );
  if (size === 'xs') {
    return hero;
  }

  const parts = (data.ByStatus ?? []).map((item) => ({ label: item.Status?.Text || '', value: item.Count, color: item.Status?.Color || 'var(--rg-muted)' }));

  const ariaLabel = t(`${k}StackAria`, parts.map((p) => `${p.label} ${p.value}`).join(', '));
  if (size === 'm') {
    // An m card is as short as an s card, only wider: the legend moves beside the bar.
    return (
      <div className="rg-ops-split">
        <div>
          {hero}
          <StackBar parts={parts} ariaLabel={ariaLabel} showLegend={false} />
          <div className="rg-ops-line">
            <a href={url}>{t(`${k}AvailableLine`, ctx.format.number(data.Available, 0), ctx.format.number(data.Unavailable + data.Delayed, 0))}</a>
          </div>
        </div>
        <Legend items={parts.map((p) => ({ label: p.label, value: ctx.format.number(p.value, 0), color: p.color }))} />
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
