import { BarList, HeroNumber, KpiGrid, Note } from '../../primitives';
import type { KpiItem, Tone } from '../../primitives';
import type { WidgetProps } from '../types';
import { ActivationHeader, deskUrl, type IncidentWidgetDataBase, NoActivation, ReadOnlyNote, wholeCurrency } from './iboShared';
import './ibo.css';

/** Core/Resgrid.Model/Dashboard/Widgets/IncidentBackOffice/IncidentFinanceWidgetData.cs */
export interface IncidentFinanceWidgetData extends IncidentWidgetDataBase {
  CostToDate?: number | null;
  DailyBurn?: number | null;
  ForecastAtCompletion?: number | null;
  ProjectedEndOn?: string | null;
  ProjectionOn?: string | null;
  CanViewBudget: boolean;
  Budget?: number | null;
  BudgetUsedPercent?: number | null;
}

function budgetTone(percent: number): Tone | undefined {
  if (percent >= 100) return 'bad';
  if (percent >= 90) return 'warn';
  return undefined;
}

/** ibo.finance (plan §7.10): an incident's cost to date against budget, the daily burn and the forecast at completion. */
export default function IncidentFinanceWidget({ data, size, t, ctx }: WidgetProps<IncidentFinanceWidgetData>) {
  const k = 'W.ibo.finance.';
  if (!data.HasActivation) {
    return <NoActivation t={t} />;
  }

  const href = deskUrl('/User/IncidentFinance', data.ActivationId);
  const currency = data.Currency || 'USD';
  const money = (value: number | null | undefined) => wholeCurrency(value, currency);
  const hasProjection = data.CostToDate !== null && data.CostToDate !== undefined;
  const kpis: KpiItem[] = [];
  if (hasProjection) {
    kpis.push({ value: money(data.DailyBurn), label: t(`${k}DailyBurn`), href });
    kpis.push({ value: money(data.ForecastAtCompletion), label: t(`${k}Forecast`) });
    if (data.ProjectedEndOn && size !== 'm') kpis.push({ value: ctx.format.date(data.ProjectedEndOn), label: t(`${k}ProjectedEnd`) });
  }
  if (data.CanViewBudget && data.Budget !== null && data.Budget !== undefined) {
    kpis.push({ value: money(data.Budget), label: t(`${k}Budget`) });
  }

  return (
    <>
      <ActivationHeader data={data} t={t} href={href} />
      {hasProjection ? (
        <HeroNumber size="md" value={money(data.CostToDate)} label={t(`${k}CostToDate`)} />
      ) : (
        <Note>{t(`${k}NoProjection`)}</Note>
      )}
      {data.BudgetUsedPercent !== null && data.BudgetUsedPercent !== undefined ? (
        <BarList
          max={100}
          items={[
            {
              label: data.Budget !== null && data.Budget !== undefined ? t(`${k}OfBudget`, money(data.Budget)) : t(`${k}Budget`),
              value: Math.min(100, data.BudgetUsedPercent),
              display: ctx.format.percent(data.BudgetUsedPercent),
              tone: budgetTone(data.BudgetUsedPercent),
              href,
            },
          ]}
        />
      ) : null}
      {kpis.length > 0 ? <KpiGrid columns={size === 'm' ? 2 : undefined} items={size === 'm' ? kpis.slice(0, 2) : kpis} /> : null}
      {hasProjection && data.ProjectionOn && size !== 'm' ? <Note>{t(`${k}ProjectionAsOf`, ctx.format.dateTime(data.ProjectionOn))}</Note> : null}
      <ReadOnlyNote readOnly={data.ReadOnly} t={t} />
    </>
  );
}
