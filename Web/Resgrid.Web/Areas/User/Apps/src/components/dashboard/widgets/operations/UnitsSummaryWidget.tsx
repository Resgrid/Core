import { Donut, EmptyState, HeroNumber, Legend } from '../../primitives';
import type { WidgetProps } from '../types';
import { Availability, type StatusChipData } from './opsShared';
import './opsWidgets.css';

/** Core/Resgrid.Model/Dashboard/Widgets/Operations/UnitsSummaryWidgetData.cs */
export interface UnitsSummaryStatus {
  Status: StatusChipData;
  Count: number;
}

export interface UnitsSummaryWidgetData {
  Total: number;
  Available: number;
  Committed: number;
  Delayed: number;
  Unavailable: number;
  Unknown: number;
  InService: number;
  ByStatus: UnitsSummaryStatus[];
  UnitsUrl?: string | null;
}

/** units.summary (plan §7.2): units by status (department colours) and by meaning (available, committed, out of service). */
export default function UnitsSummaryWidget({ data, size, t, ctx }: WidgetProps<UnitsSummaryWidgetData>) {
  const k = 'W.units.summary.';
  const url = data.UnitsUrl ?? '/User/Units/Index';

  if (!data.Total) {
    return <EmptyState icon="fa-road" text={t(`${k}Empty`)} linkText={t(`${k}OpenUnits`)} href={url} />;
  }

  if (size === 'xs') {
    return (
      <a className="rg-ops-herolink" href={url}>
        <HeroNumber
          value={`${ctx.format.number(data.Available, 0)}/${ctx.format.number(data.Total, 0)}`}
          label={t(`${k}AvailableOf`)}
          size="md"
        />
      </a>
    );
  }

  const statuses = data.ByStatus ?? [];
  const parts = statuses.map((s) => ({ label: s.Status?.Text || t(`${k}NoStatus`), value: s.Count, color: s.Status?.Color || 'var(--rg-muted)' }));
  const donut = (
    <Donut
      parts={parts}
      ariaLabel={t(`${k}DonutAria`, parts.map((p) => `${p.label} ${p.value}`).join(', '))}
      centerLabel={t(`${k}Units`)}
      showLegend={false}
      size={size === 'm' ? 104 : 92}
    />
  );

  if (size === 'm') {
    return (
      <div className="rg-ops-donutrow">
        {donut}
        <div className="rg-ops-grow">
          <Legend items={parts.map((p) => ({ label: p.label, value: ctx.format.number(p.value, 0), color: p.color }))} />
          <div className="rg-ops-line">{t(`${k}InServiceLine`, ctx.format.number(data.InService, 0), ctx.format.number(data.Unavailable, 0))}</div>
        </div>
      </div>
    );
  }

  // A meaning takes the colour of the department's most used status with that meaning, so nothing is hard-coded.
  const colorOf = (...classes: number[]) => statuses.find((s) => s.Status != null && classes.includes(s.Status.Availability))?.Status?.Color || 'var(--rg-muted)';
  return (
    <div className="rg-ops-donutrow">
      {donut}
      <Legend
        items={[
          { label: t(`${k}Available`), value: ctx.format.number(data.Available, 0), color: colorOf(Availability.Available) },
          { label: t(`${k}Committed`), value: ctx.format.number(data.Committed + data.Delayed, 0), color: colorOf(Availability.Committed, Availability.Delayed) },
          { label: t(`${k}OutOfService`), value: ctx.format.number(data.Unavailable, 0), color: colorOf(Availability.Unavailable) },
        ]}
      />
    </div>
  );
}
