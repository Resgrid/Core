import { EmptyState, ItemList, Note } from '../../primitives';
import type { WidgetProps } from '../types';

/** Core/Resgrid.Model/Dashboard/Widgets/Business/WorkforceFieldCostingWidgetData.cs */
export interface WorkforceFieldCostingWidgetData {
  Runs: WorkforceFieldCostRunRow[];
  OpenRuns: number;
  MissingInputs: number;
}

export interface WorkforceFieldCostRunRow {
  FieldCostRunId: string;
  /** Bid, Call, Deployment. */
  ContextType: string;
  /** Draft, NeedsReview, Frozen, Superseded. */
  Status: string;
  Currency?: string | null;
  TotalLoadedCost: number;
  RevenueAmount?: number | null;
  ContributionMargin?: number | null;
  ContributionMarginPercent?: number | null;
  MissingInputCount: number;
  ThroughDate?: string | null;
  AddedOn: string;
}

const RUNS_URL = '/User/Workforce/CostRuns';

/** workforce.fieldCosting (plan §7.9): the newest field cost runs with loaded cost, margin and missing inputs. */
export default function WorkforceFieldCostingWidget({ data, t, ctx }: WidgetProps<WorkforceFieldCostingWidgetData>) {
  const k = 'W.workforce.fieldCosting.';
  const runs = data.Runs ?? [];
  if (runs.length === 0) {
    return <EmptyState icon="fa-calculator" text={t(`${k}Empty`)} linkText={t(`${k}OpenRuns`)} href={RUNS_URL} />;
  }

  return (
    <>
      <ItemList
        items={runs.map((run) => {
          const currency = run.Currency || 'USD';
          const margin =
            run.ContributionMarginPercent !== null && run.ContributionMarginPercent !== undefined
              ? `${t(`${k}Margin`)} ${ctx.format.percent(run.ContributionMarginPercent, 1)}`
              : null;
          return {
            key: run.FieldCostRunId,
            title: `${t(`${k}Context.${run.ContextType}`)} · ${t(`${k}Status.${run.Status}`)}`,
            meta: [
              run.ThroughDate ? ctx.format.date(run.ThroughDate) : ctx.format.date(run.AddedOn),
              margin,
              run.MissingInputCount ? `${t(`${k}MissingInputs`)} ${run.MissingInputCount}` : null,
            ]
              .filter(Boolean)
              .join(' · '),
            tone: run.MissingInputCount ? 'warn' : undefined,
            right: <b className="rg-num">{ctx.format.currency(run.TotalLoadedCost, currency)}</b>,
            href: RUNS_URL,
          };
        })}
      />
      {data.OpenRuns ? <Note>{t(`${k}OpenRunsNote`, data.OpenRuns)}</Note> : null}
    </>
  );
}
